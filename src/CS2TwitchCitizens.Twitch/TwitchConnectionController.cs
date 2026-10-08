using System.Diagnostics;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using CS2TwitchCitizens.Commands;

namespace CS2TwitchCitizens.Twitch;

[DataContract]
public sealed class TwitchAuthView
{
    [DataMember(Name = "state")] public string State { get; set; } = "Disconnected";
    [DataMember(Name = "login")] public string Login { get; set; } = "";
    [DataMember(Name = "displayName")] public string DisplayName { get; set; } = "";
    [DataMember(Name = "userCode")] public string UserCode { get; set; } = "";
    [DataMember(Name = "verificationUri")] public string VerificationUri { get; set; } = "";
    [DataMember(Name = "expiresAt")] public long ExpiresAt { get; set; }
    [DataMember(Name = "error")] public string Error { get; set; } = "";
    [DataMember(Name = "eventSubStatus")] public string EventSubStatus { get; set; } = "Disabled";
    [DataMember(Name = "legacyConfig")] public bool LegacyConfig { get; set; }
    public string ToJson()
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(TwitchAuthView)).WriteObject(stream, this);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>Owns one OAuth operation and one EventSub service. No ECS dependencies.</summary>
public sealed class TwitchConnectionController : IDisposable
{
    private readonly object _gate = new object();
    private readonly string _clientId;
    private readonly TwitchCredentialStore _store;
    private readonly TwitchOAuthClient _oauth;
    private readonly TwitchCommandQueue _queue;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
    private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
    private CancellationTokenSource? _authorization;
    private TwitchEventSubService? _eventSub;
    private TwitchCredentials? _credentials;
    private TwitchAuthView _view = new TwitchAuthView();
    private Task? _maintenance;
    private DateTimeOffset _lastValidation;
    private bool _disposed;
    private int _generation;

    public TwitchConnectionController(string clientId, TwitchCredentialStore store, TwitchOAuthClient oauth,
        TwitchCommandQueue queue, Action<string> log, bool legacyConfig)
    {
        _clientId = clientId.Trim(); _store = store; _oauth = oauth; _queue = queue; _log = log;
        _view.LegacyConfig = legacyConfig;
    }

    public TwitchAuthView View
    {
        get { lock (_gate) return new TwitchAuthView {
            State = _view.State, Login = _view.Login, DisplayName = _view.DisplayName, UserCode = _view.UserCode,
            VerificationUri = _view.VerificationUri, ExpiresAt = _view.ExpiresAt,
            Error = _view.Error, LegacyConfig = _view.LegacyConfig,
            EventSubStatus = _eventSub?.Status.ToString() ?? "Disabled"
        }; }
    }
    public string UserId { get { lock (_gate) return _credentials?.UserId ?? ""; } }

    public void Start()
    {
        if (!string.IsNullOrWhiteSpace(_clientId))
        {
            if (_store.Exists) lock (_gate) _view.State = "Restoring";
        }
        _maintenance = Task.Run(() => MaintainAsync(_lifetime.Token));
    }

    public void Connect()
    {
        lock (_gate)
        {
            if (_disposed || _authorization != null || _view.State == "Connected" || _view.State == "Restoring") return;
            if (string.IsNullOrWhiteSpace(_clientId)) { _view.State = "Error"; _view.Error = "ClientIdMissing"; return; }
            _authorization = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _view.State = "Requesting"; _view.Error = "";
            _ = AuthorizeAsync(_authorization, _generation);
        }
    }

    private async Task AuthorizeAsync(CancellationTokenSource session, int generation)
    {
        try
        {
            var cancellation = session.Token;
            var code = await _oauth.RequestDeviceAsync(_clientId, cancellation).ConfigureAwait(false);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(code.ExpiresIn);
            lock (_gate) { if (cancellation.IsCancellationRequested) return;
                _view.State = "Pending"; _view.UserCode = code.UserCode; _view.VerificationUri = code.VerificationUri;
                _view.ExpiresAt = deadline.ToUnixTimeSeconds(); }
            var interval = TimeSpan.FromSeconds(code.Interval);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(interval, cancellation).ConfigureAwait(false);
                if (DateTimeOffset.UtcNow >= deadline) break;
                try
                {
                    var token = await _oauth.PollAsync(_clientId, code.DeviceCode, cancellation).ConfigureAwait(false);
                    if (token == null) continue;
                    var identity = await _oauth.ValidateAsync(_clientId, token.AccessToken, cancellation).ConfigureAwait(false);
                    var credentials = MakeCredentials(token, identity);
                    credentials.DisplayName = await _oauth.GetDisplayNameAsync(_clientId, token.AccessToken, cancellation).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (cancellation.IsCancellationRequested || generation != _generation) return;
                        _store.Save(credentials);
                        Activate(credentials);
                    }
                    return;
                }
                catch (TwitchRateLimitException) { interval += TimeSpan.FromSeconds(5); }
            }
            SetError("CodeExpired");
        }
        catch (OperationCanceledException) { }
        catch (UnauthorizedAccessException) { SetError("Denied"); }
        catch (TimeoutException) { SetError("CodeExpired"); }
        catch (Exception ex) { _log("[CS2TwitchCitizens] Twitch authorization failed: " + ex.GetType().Name); SetError(ex is IOException ? "StorageError" : "NetworkError"); }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_authorization, session)) _authorization = null; }
            session.Dispose();
        }
    }

    private static TwitchCredentials MakeCredentials(TwitchToken token, TwitchIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken) || token.ExpiresIn <= 0)
            throw new InvalidDataException("Incomplete Twitch token response.");
        return new TwitchCredentials { AccessToken = token.AccessToken, RefreshToken = token.RefreshToken,
            UserId = identity.UserId, Login = identity.Login, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn) };
    }

    private void Activate(TwitchCredentials credentials)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _eventSub?.Dispose();
            if (_credentials != null && _credentials.UserId != credentials.UserId) _queue.Clear();
            _credentials = credentials;
            _view.State = "Connected"; _view.Login = credentials.Login; _view.DisplayName = credentials.DisplayName; _view.Error = "";
            _view.UserCode = ""; _view.VerificationUri = ""; _view.ExpiresAt = 0;
            _eventSub = new TwitchEventSubService(new TwitchConfig {
                Enabled = true, ClientId = _clientId, AccessToken = credentials.AccessToken,
                BroadcasterUserId = credentials.UserId, UserId = credentials.UserId
            }, _queue, _log);
            _eventSub.Start();
            _lastValidation = DateTimeOffset.UtcNow;
        }
    }

    private async Task MaintainAsync(CancellationToken cancellation)
    {
        try
        {
            TwitchCredentials? restoreCandidate = null;
            int restoreGeneration = -1;
            try
            {
                int initialGeneration;
                lock (_gate) initialGeneration = _generation;
                var saved = _store.Load();
                if (saved != null)
                {
                    bool current;
                    lock (_gate) { current = initialGeneration == _generation; if (current) _view.State = "Restoring"; }
                    if (current)
                    {
                        restoreCandidate = saved;
                        restoreGeneration = initialGeneration;
                        await RestoreAsync(saved, cancellation, false, initialGeneration).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) { _log("[CS2TwitchCitizens] Twitch restore failed: " + ex.GetType().Name); SetError("StorageError"); }
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellation).ConfigureAwait(false);
                TwitchCredentials? current;
                TwitchConnectionStatus status;
                int generation;
                DateTimeOffset lastValidation;
                string state;
                string error;
                lock (_gate) { current = _credentials; status = _eventSub?.Status ?? TwitchConnectionStatus.Disabled;
                    generation = _generation; lastValidation = _lastValidation; state = _view.State; error = _view.Error; }
                if (current == null)
                {
                    if (restoreCandidate != null && restoreGeneration == generation && state == "Error" && error == "NetworkError")
                        await RestoreAsync(restoreCandidate, cancellation, false, generation).ConfigureAwait(false);
                    continue;
                }
                restoreCandidate = null;
                if (current.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(10) || status == TwitchConnectionStatus.AuthenticationError)
                    await RestoreAsync(current, cancellation, true, generation).ConfigureAwait(false);
                else if (status == TwitchConnectionStatus.Disabled || DateTimeOffset.UtcNow - lastValidation >= TimeSpan.FromHours(1))
                    await RestoreAsync(current, cancellation, false, generation).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RestoreAsync(TwitchCredentials saved, CancellationToken cancellation, bool forceRefresh = false, int generation = -1)
    {
        await _refreshGate.WaitAsync(cancellation).ConfigureAwait(false);
        try { await RestoreCoreAsync(saved, cancellation, forceRefresh, generation).ConfigureAwait(false); }
        finally { _refreshGate.Release(); }
    }

    private async Task RestoreCoreAsync(TwitchCredentials saved, CancellationToken cancellation, bool forceRefresh, int generation)
    {
        lock (_gate)
        {
            if (generation != _generation || _disposed) return;
            if (_credentials != null && _credentials.UserId == saved.UserId) saved = _credentials;
        }
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                TwitchCredentials current = saved;
                if (forceRefresh || saved.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(10))
                {
                    var token = await _oauth.RefreshAsync(_clientId, saved.RefreshToken, cancellation).ConfigureAwait(false);
                    var identity = await _oauth.ValidateAsync(_clientId, token.AccessToken, cancellation).ConfigureAwait(false);
                    if (identity.UserId != saved.UserId) throw new UnauthorizedAccessException("Twitch identity changed.");
                    current = MakeCredentials(token, identity);
                    current.DisplayName = saved.DisplayName;
                    lock (_gate) { if (generation != _generation || _disposed) return; _store.Save(current); }
                }
                else
                {
                    TwitchIdentity identity;
                    try { identity = await _oauth.ValidateAsync(_clientId, saved.AccessToken, cancellation).ConfigureAwait(false); }
                    catch (UnauthorizedAccessException)
                    {
                        var token = await _oauth.RefreshAsync(_clientId, saved.RefreshToken, cancellation).ConfigureAwait(false);
                        identity = await _oauth.ValidateAsync(_clientId, token.AccessToken, cancellation).ConfigureAwait(false);
                        if (identity.UserId != saved.UserId) throw new UnauthorizedAccessException("Twitch identity changed.");
                        current = MakeCredentials(token, identity);
                        current.DisplayName = saved.DisplayName;
                        lock (_gate) { if (generation != _generation || _disposed) return; _store.Save(current); }
                    }
                    if (identity.UserId != saved.UserId) throw new UnauthorizedAccessException("Twitch identity changed.");
                }
                lock (_gate)
                {
                    if (generation != _generation || _disposed || (_credentials != null && _credentials.UserId != saved.UserId)) return;
                    if (!ReferenceEquals(current, saved) || _credentials == null || _eventSub == null ||
                        _eventSub.Status == TwitchConnectionStatus.AuthenticationError)
                        Activate(current);
                    _lastValidation = DateTimeOffset.UtcNow;
                }
                return;
            }
            catch (UnauthorizedAccessException) { lock (_gate) { if (generation != _generation) return;
                _eventSub?.Dispose(); _eventSub = null; _credentials = null; } SetError("Reauthorize"); return; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _log("[CS2TwitchCitizens] Twitch token maintenance failed: " + ex.GetType().Name);
                if (ex is IOException || ex is System.Security.Cryptography.CryptographicException)
                { lock (_gate) { if (generation == _generation) SetError("StorageError"); } return; }
                if (attempt == 2) { lock (_gate) { if (generation == _generation) SetError("NetworkError"); } return; }
                await Task.Delay(TimeSpan.FromSeconds(2 << attempt), cancellation).ConfigureAwait(false);
            }
        }
    }

    public void Cancel()
    {
        lock (_gate) { _authorization?.Cancel(); if (_view.State == "Pending" || _view.State == "Requesting")
            { _view.State = "Disconnected"; _view.UserCode = ""; _view.VerificationUri = ""; _view.ExpiresAt = 0; } }
    }
    public void Reconnect()
    {
        TwitchCredentials? saved;
        int generation;
        lock (_gate) { saved = _credentials; generation = _generation; _eventSub?.Dispose(); _eventSub = null; }
        if (saved != null) _ = Task.Run(async () => {
            try { await RestoreAsync(saved, _lifetime.Token, false, generation).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        });
        else Connect();
    }
    public void Disconnect()
    {
        TwitchCredentials? saved;
        lock (_gate) { _generation++; _authorization?.Cancel(); saved = _credentials; _credentials = null;
            _eventSub?.Dispose(); _eventSub = null; _view = new TwitchAuthView { LegacyConfig = _view.LegacyConfig }; }
        _queue.Clear();
        try { _store.Delete(); }
        catch (Exception ex) { _log("[CS2TwitchCitizens] Twitch credential removal failed: " + ex.GetType().Name);
            SetError("StorageError"); }
        if (saved != null) _ = Task.Run(async () => { try { await _oauth.RevokeAsync(_clientId, saved.AccessToken, _lifetime.Token).ConfigureAwait(false); }
            catch (Exception ex) { _log("[CS2TwitchCitizens] Twitch revocation failed: " + ex.GetType().Name); } });
    }
    public void OpenVerification()
    {
        string uri;
        lock (_gate) uri = _view.VerificationUri;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == "https" && parsed.Host == "www.twitch.tv")
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
    private void SetError(string error) { lock (_gate) { if (_disposed) return; _view.State = "Error"; _view.Error = error;
        _view.UserCode = ""; _view.VerificationUri = ""; _view.ExpiresAt = 0; } }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _authorization?.Cancel();
            _eventSub?.Dispose(); _eventSub = null; }
        if (_maintenance == null) _lifetime.Dispose();
        else _maintenance.ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
        _oauth.Dispose();
    }
}
