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
    [DataMember(Name = "writePermission")] public string WritePermission { get; set; } = "AuthorizationRequired";
    [DataMember(Name = "reauthorizationState")] public string ReauthorizationState { get; set; } = "Idle";
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
    private readonly CommandSettingsController? _settings;
    private readonly TwitchChatSender _chatSender = new TwitchChatSender();
    private readonly Queue<OutgoingReply> _replies = new Queue<OutgoingReply>();
    private CancellationTokenSource _replyScope = new CancellationTokenSource();
    private int _replyEpoch;
    private Task? _replyWorker;
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
        TwitchCommandQueue queue, Action<string> log, bool legacyConfig, CommandSettingsController? settings = null)
    {
        _clientId = clientId.Trim(); _store = store; _oauth = oauth; _queue = queue; _log = log;
        _view.LegacyConfig = legacyConfig;
        _settings = settings;
    }

    public TwitchAuthView View
    {
        get { lock (_gate) return new TwitchAuthView {
            State = _view.State, Login = _view.Login, DisplayName = _view.DisplayName, UserCode = _view.UserCode,
            VerificationUri = _view.VerificationUri, ExpiresAt = _view.ExpiresAt,
            Error = _view.Error, LegacyConfig = _view.LegacyConfig,
            WritePermission = _view.WritePermission, ReauthorizationState = _view.ReauthorizationState,
            EventSubStatus = _eventSub?.Status.ToString() ?? "Disabled"
        }; }
    }
    public string UserId { get { lock (_gate) return _credentials?.UserId ?? ""; } }

    public void Start()
    {
        if (!string.IsNullOrWhiteSpace(_clientId))
        {
            if (_store.Exists) lock (_gate) { _view.State = "Restoring"; _view.WritePermission = "Checking"; }
        }
        _maintenance = Task.Run(() => MaintainAsync(_lifetime.Token));
        _replyWorker = Task.Run(() => SendRepliesAsync(_lifetime.Token));
    }

    public bool QueueReply(TwitchCommand command, string message)
    {
        var settings = _settings?.Current ?? CommandSettings.Defaults();
        if (!settings.ResponsesEnabled) return SkipReply("responses-disabled");
        if (!settings.For(command.Command).ResponseEnabled) return SkipReply("command-response-disabled");
        if (string.IsNullOrWhiteSpace(message)) return SkipReply("empty-message");
        lock (_gate)
        {
            if (_disposed) return SkipReply("disposed");
            if (_credentials == null) return SkipReply("no-credentials");
            if (_view.WritePermission != "Allowed") return SkipReply("write-permission-unavailable");
            if (_view.State != "Connected") return SkipReply("authorization-incomplete");
            if (_eventSub?.Status != TwitchConnectionStatus.Connected) return SkipReply("eventsub-disconnected");
            if (_replies.Count >= settings.MaxOutgoingQueue) return SkipReply("outgoing-queue-full");
            _replies.Enqueue(new OutgoingReply(message, command.MessageId, _generation, _replyEpoch));
            return true;
        }
    }

    private bool SkipReply(string reason)
    {
        _log("[CS2TwitchCitizens] Twitch chat reply skipped: " + reason);
        return false;
    }

    public void ClearReplies() { lock (_gate) ClearRepliesLocked(); }

    private void ClearRepliesLocked()
    {
        _replies.Clear();
        _replyEpoch++;
        var previous = _replyScope;
        _replyScope = new CancellationTokenSource();
        previous.Cancel();
        previous.Dispose();
    }

    private async Task SendRepliesAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            OutgoingReply? reply = null;
            TwitchCredentials? credentials;
            int generation;
            int epoch;
            CancellationToken scopeToken;
            lock (_gate) { credentials = _view.State == "Connected" && _view.WritePermission == "Allowed" &&
                    _eventSub?.Status == TwitchConnectionStatus.Connected ? _credentials : null;
                reply = credentials != null && _replies.Count > 0 ? _replies.Dequeue() : null;
                generation = _generation; epoch = _replyEpoch; scopeToken = _replyScope.Token; }
            if (reply == null || credentials == null || reply.Generation != generation || reply.Epoch != epoch)
            {
                try { await Task.Delay(250, cancellation).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, scopeToken);
                var result = await _chatSender.SendAsync(_clientId, credentials.AccessToken, credentials.UserId,
                    credentials.UserId, reply.Message, reply.ParentId, linked.Token).ConfigureAwait(false);
                if (result == ChatSendResult.Unauthorized) RequireWriteAuthorization("chat-http-401");
                if (result != ChatSendResult.Sent)
                    _log("[CS2TwitchCitizens] Twitch chat send result=" + result);
                var seconds = result == ChatSendResult.RateLimited ? 30 :
                    (_settings?.Current.OutgoingIntervalSeconds ?? 2);
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { if (cancellation.IsCancellationRequested) return; }
            catch (Exception ex) { _log("[CS2TwitchCitizens] Twitch chat send error=" + ex.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(3), cancellation).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; } }
        }
    }

    private sealed class OutgoingReply
    {
        public OutgoingReply(string message, string parentId, int generation, int epoch)
        { Message = message; ParentId = parentId; Generation = generation; Epoch = epoch; }
        public string Message { get; }
        public string ParentId { get; }
        public int Generation { get; }
        public int Epoch { get; }
    }

    public void Connect()
    {
        lock (_gate)
        {
            if (_disposed || _authorization != null || _view.State == "Connected" || _view.State == "Restoring") return;
            if (string.IsNullOrWhiteSpace(_clientId)) { _view.State = "Error"; _view.Error = "ClientIdMissing"; return; }
            _authorization = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _view.State = "Requesting"; _view.WritePermission = "Checking"; _view.Error = "";
            _ = AuthorizeAsync(_authorization, _generation, false);
        }
    }

    public void Reauthorize()
    {
        lock (_gate)
        {
            if (_disposed || _authorization != null) return;
            if (_credentials == null) { Connect(); return; }
            if (string.IsNullOrWhiteSpace(_clientId)) { _view.Error = "ClientIdMissing"; return; }
            _authorization = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _view.ReauthorizationState = "Requesting"; _view.Error = "";
            _ = AuthorizeAsync(_authorization, _generation, true);
        }
    }

    private async Task AuthorizeAsync(CancellationTokenSource session, int generation, bool reauthorize)
    {
        try
        {
            var cancellation = session.Token;
            var code = await _oauth.RequestDeviceAsync(_clientId, cancellation).ConfigureAwait(false);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(code.ExpiresIn);
            lock (_gate) { if (cancellation.IsCancellationRequested || generation != _generation) return;
                if (reauthorize) _view.ReauthorizationState = "Pending";
                else _view.State = "Pending";
                _view.UserCode = code.UserCode; _view.VerificationUri = code.VerificationUri;
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
                    lock (_gate) { if (cancellation.IsCancellationRequested || generation != _generation) return;
                        _view.WritePermission = "Checking"; }
                    var identity = await _oauth.ValidateAsync(_clientId, token.AccessToken, cancellation).ConfigureAwait(false);
                    var credentials = MakeCredentials(token, identity);
                    credentials.DisplayName = await _oauth.GetDisplayNameAsync(_clientId, token.AccessToken, cancellation).ConfigureAwait(false);
                    await _refreshGate.WaitAsync(cancellation).ConfigureAwait(false);
                    try { lock (_gate)
                    {
                        if (cancellation.IsCancellationRequested || generation != _generation) return;
                        if (reauthorize && _credentials != null && identity.UserId != _credentials.UserId)
                            throw new UnauthorizedAccessException("Twitch identity changed.");
                        _store.Save(credentials);
                        Activate(credentials, "Allowed");
                    } }
                    finally { _refreshGate.Release(); }
                    return;
                }
                catch (TwitchRateLimitException) { interval += TimeSpan.FromSeconds(5); }
            }
            SetAuthorizationError("CodeExpired", reauthorize, generation);
        }
        catch (OperationCanceledException) { }
        catch (UnauthorizedAccessException) { SetAuthorizationError("Denied", reauthorize, generation); }
        catch (TimeoutException) { SetAuthorizationError("CodeExpired", reauthorize, generation); }
        catch (Exception ex) { _log("[CS2TwitchCitizens] Twitch authorization failed: " + ex.GetType().Name);
            SetAuthorizationError(ex is IOException ? "StorageError" : "NetworkError", reauthorize, generation); }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_authorization, session)) _authorization = null; }
            session.Dispose();
        }
    }

    private void SetAuthorizationError(string error, bool reauthorize, int generation)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation) return;
            if (!reauthorize) { SetError(error); return; }
            _view.ReauthorizationState = "Idle"; _view.Error = error;
            _view.WritePermission = "AuthorizationRequired";
            _view.UserCode = ""; _view.VerificationUri = ""; _view.ExpiresAt = 0;
        }
    }

    private static TwitchCredentials MakeCredentials(TwitchToken token, TwitchIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken) || token.ExpiresIn <= 0)
            throw new InvalidDataException("Incomplete Twitch token response.");
        return new TwitchCredentials { AccessToken = token.AccessToken, RefreshToken = token.RefreshToken,
            UserId = identity.UserId, Login = identity.Login, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn) };
    }

    private void Activate(TwitchCredentials credentials, string writePermission)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _eventSub?.Dispose();
            ClearRepliesLocked();
            if (_credentials != null && _credentials.UserId != credentials.UserId) _queue.Clear();
            _credentials = credentials;
            _view.State = "Connected"; _view.Login = credentials.Login; _view.DisplayName = credentials.DisplayName; _view.Error = "";
            _view.WritePermission = writePermission; _view.ReauthorizationState = "Idle";
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
                lock (_gate) { current = _authorization == null ? _credentials : null;
                    status = _eventSub?.Status ?? TwitchConnectionStatus.Disabled;
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
                TwitchIdentity identity;
                if (forceRefresh || saved.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(10))
                {
                    var token = await _oauth.RefreshAsync(_clientId, saved.RefreshToken, cancellation).ConfigureAwait(false);
                    identity = await _oauth.ValidateAsync(_clientId, token.AccessToken, cancellation, false).ConfigureAwait(false);
                    if (identity.UserId != saved.UserId) throw new UnauthorizedAccessException("Twitch identity changed.");
                    current = MakeCredentials(token, identity);
                    current.DisplayName = saved.DisplayName;
                    lock (_gate) { if (generation != _generation || _disposed) return; _store.Save(current); }
                }
                else
                {
                    try { identity = await _oauth.ValidateAsync(_clientId, saved.AccessToken, cancellation, false).ConfigureAwait(false); }
                    catch (UnauthorizedAccessException)
                    {
                        var token = await _oauth.RefreshAsync(_clientId, saved.RefreshToken, cancellation).ConfigureAwait(false);
                        identity = await _oauth.ValidateAsync(_clientId, token.AccessToken, cancellation, false).ConfigureAwait(false);
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
                    var writePermission = identity.Scopes.Contains("user:write:chat") ? "Allowed" : "AuthorizationRequired";
                    if (!ReferenceEquals(current, saved) || _credentials == null || _eventSub == null ||
                        _eventSub.Status == TwitchConnectionStatus.AuthenticationError)
                        Activate(current, writePermission);
                    else _view.WritePermission = writePermission;
                    if (writePermission != "Allowed") ClearRepliesLocked();
                    _lastValidation = DateTimeOffset.UtcNow;
                }
                return;
            }
            catch (UnauthorizedAccessException) { lock (_gate) { if (generation != _generation) return;
                _eventSub?.Dispose(); _eventSub = null; _credentials = null;
                _view.WritePermission = "AuthorizationRequired"; } SetError("Reauthorize"); return; }
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
        lock (_gate) { var reauthorizing = _view.ReauthorizationState != "Idle";
            _authorization?.Cancel(); if (_view.State == "Pending" || _view.State == "Requesting")
            _view.State = "Disconnected";
            _view.ReauthorizationState = "Idle";
            if (_credentials == null || reauthorizing) _view.WritePermission = "AuthorizationRequired";
            _view.UserCode = ""; _view.VerificationUri = ""; _view.ExpiresAt = 0; }
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
            ClearRepliesLocked();
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
    private void SetError(string error) { lock (_gate) { if (_disposed) return; ClearRepliesLocked(); _view.State = "Error"; _view.Error = error;
        _view.WritePermission = "AuthorizationRequired";
        _view.UserCode = ""; _view.VerificationUri = ""; _view.ExpiresAt = 0; } }

    private void RequireWriteAuthorization(string reason)
    {
        lock (_gate) { if (_disposed) return; ClearRepliesLocked(); _view.WritePermission = "AuthorizationRequired"; }
        _log("[CS2TwitchCitizens] Twitch chat reply unavailable: " + reason);
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _authorization?.Cancel();
            ClearRepliesLocked();
            _eventSub?.Dispose(); _eventSub = null; }
        if (_maintenance == null) _lifetime.Dispose();
        else _maintenance.ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
        _oauth.Dispose();
        if (_replyWorker == null) { _chatSender.Dispose(); _replyScope.Dispose(); }
        else _replyWorker.ContinueWith(_ => { _chatSender.Dispose(); _replyScope.Dispose(); }, TaskScheduler.Default);
    }
}
