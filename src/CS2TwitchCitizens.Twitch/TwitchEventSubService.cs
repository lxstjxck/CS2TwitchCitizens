using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Authentication;
using System.Text;
using CS2TwitchCitizens.Commands;

namespace CS2TwitchCitizens.Twitch;

/// <summary>Network-only EventSub client. Its only game-facing handoff is TwitchCommandQueue.</summary>
public sealed class TwitchEventSubService : IDisposable
{
    private static readonly Uri Endpoint = new Uri("wss://eventsub.wss.twitch.tv/ws");
    private static readonly Uri ValidateEndpoint = new Uri("https://id.twitch.tv/oauth2/validate");
    private static readonly Uri SubscriptionsEndpoint = new Uri("https://api.twitch.tv/helix/eventsub/subscriptions");
    private readonly TwitchConfig _config;
    private readonly TwitchCommandQueue _queue;
    private readonly RedemptionQueue? _redemptions;
    private readonly Action<string> _log;
    private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    private readonly CancellationTokenSource _stop = new CancellationTokenSource();
    private readonly EventSubMessageIds _messageIds = new EventSubMessageIds();
    private Dictionary<string, string> _rewardIcons = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly object _socketGate = new object();
    private ClientWebSocket? _socket;
    private Task? _worker;
    private bool _disposed;
    private DateTimeOffset _validatedAt;
    private volatile TwitchConnectionStatus _status = TwitchConnectionStatus.Disconnected;
    private volatile string _redemptionStatus = "Disconnected";
    private bool _hasRedemptionScope;

    public TwitchConnectionStatus Status => _status;
    public string RedemptionStatus => _redemptionStatus;

    public TwitchEventSubService(TwitchConfig config, TwitchCommandQueue queue, Action<string> log, RedemptionQueue? redemptions = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _redemptions = redemptions;
        if (!config.Enabled || !config.IsComplete)
            throw new ArgumentException("Enabled Twitch config must be complete.", nameof(config));
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TwitchEventSubService));
        if (_worker != null) throw new InvalidOperationException("Twitch service already started.");
        _status = TwitchConnectionStatus.Connecting;
        _worker = Task.Run(() => RunAsync(_stop.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ValidateTokenAsync(cancellationToken).ConfigureAwait(false);
                await ConnectAndListenAsync(cancellationToken).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested)
                    _log("[CS2TwitchCitizens] Twitch WebSocket disconnected");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _status = TwitchConnectionStatus.Disconnected;
                return;
            }
            catch (AuthenticationException)
            {
                _status = TwitchConnectionStatus.AuthenticationError;
                _redemptionStatus = "AuthenticationError";
                return;
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                    _log($"[CS2TwitchCitizens] Twitch connection error={ex.GetType().Name}");
            }

            if (cancellationToken.IsCancellationRequested)
                return;
            failures++;
            _status = TwitchConnectionStatus.Reconnecting;
            if (_hasRedemptionScope) _redemptionStatus = "Reconnecting";
            var delay = EventSubRetryPolicy.Delay(failures);
            _log($"[CS2TwitchCitizens] Twitch reconnect in {delay.TotalSeconds:0}s");
            try { await Task.Delay(delay, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
        _status = TwitchConnectionStatus.Disconnected;
    }

    private async Task ValidateTokenAsync(CancellationToken cancellationToken)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get, ValidateEndpoint))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", _config.AccessToken);
            using (var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _log("[CS2TwitchCitizens] Twitch token invalid or expired (HTTP 401)");
                    throw new AuthenticationException("Twitch token invalid.");
                }
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("Twitch token validation HTTP " + (int)response.StatusCode);

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var data = (TokenValidation?)new DataContractJsonSerializer(typeof(TokenValidation)).ReadObject(stream);
                    if (data?.ClientId != _config.ClientId || data.UserId != _config.UserId ||
                        data.Scopes == null || !data.Scopes.Contains("user:read:chat"))
                    {
                        _log("[CS2TwitchCitizens] Twitch token client ID, user ID, or user:read:chat scope mismatch");
                        throw new AuthenticationException("Twitch token does not match config.");
                    }
                    _hasRedemptionScope = data.Scopes.Contains("channel:read:redemptions") ||
                        data.Scopes.Contains("channel:manage:redemptions");
                    if (!_hasRedemptionScope) _redemptionStatus = "AuthorizationRequired";
                }
            }
        }
        _validatedAt = DateTimeOffset.UtcNow;
    }

    private async Task ConnectAndListenAsync(CancellationToken cancellationToken)
    {
        var socket = await ConnectAsync(Endpoint, cancellationToken).ConfigureAwait(false);
        try
        {
            var welcome = await ReadAsync(socket, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            if (welcome?.Metadata?.MessageType != "session_welcome" || string.IsNullOrWhiteSpace(welcome.Payload?.Session?.Id))
                throw new InvalidDataException("Twitch EventSub Welcome missing.");
            _log("[CS2TwitchCitizens] Twitch EventSub session established");
            await SubscribeAsync(welcome.Payload!.Session!.Id!, cancellationToken).ConfigureAwait(false);
            if (_hasRedemptionScope) _redemptionStatus = "Connecting";
            if (_redemptions != null && _redemptionStatus == "Connecting")
            {
                try { await SubscribeRedemptionsAsync(welcome.Payload.Session.Id!, cancellationToken).ConfigureAwait(false);
                    await SubscribeRedemptionUpdatesAsync(welcome.Payload.Session.Id!, cancellationToken).ConfigureAwait(false);
                    _redemptionStatus = "Connected"; }
                catch (UnauthorizedAccessException ex) { _redemptionStatus = "SubscriptionError";
                    _log("[CS2TwitchCitizens] Channel Points subscription failed: " + ex.GetType().Name); }
            }
            if (_redemptions != null && _redemptionStatus == "Connected")
            {
                try { _rewardIcons = await TwitchRewardCatalog.LoadAsync(_http, _config.ClientId,
                    _config.AccessToken, _config.BroadcasterUserId, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _log("[CS2TwitchCitizens] Reward icon lookup failed: " + ex.GetType().Name); }
            }
            _status = TwitchConnectionStatus.Connected;
            var keepalive = KeepaliveTimeout(welcome);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow - _validatedAt >= TimeSpan.FromHours(1))
                    await ValidateTokenAsync(cancellationToken).ConfigureAwait(false);

                var envelope = await ReadAsync(socket, keepalive, cancellationToken).ConfigureAwait(false);
                if (envelope == null)
                    return;

                if (envelope.Metadata?.MessageType == "session_reconnect")
                {
                    if (!EventSubProtocol.TryGetReconnectUri(envelope, out var uri))
                        throw new InvalidDataException("Invalid Twitch reconnect URL.");
                    var replacement = await ConnectAsync(uri!, cancellationToken).ConfigureAwait(false);
                    _status = TwitchConnectionStatus.Reconnecting;
                    var transferred = false;
                    try
                    {
                        var nextWelcome = await ReadAsync(replacement, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                        if (nextWelcome?.Metadata?.MessageType != "session_welcome" || string.IsNullOrWhiteSpace(nextWelcome.Payload?.Session?.Id))
                            throw new InvalidDataException("Twitch reconnect Welcome missing.");
                        keepalive = KeepaliveTimeout(nextWelcome);
                        socket.Dispose();
                        socket = replacement;
                        transferred = true;
                        _log("[CS2TwitchCitizens] Twitch EventSub session reconnected");
                        _status = TwitchConnectionStatus.Connected;
                    }
                    finally
                    {
                        if (!transferred) replacement.Dispose();
                    }
                    continue;
                }

                if (envelope.Metadata?.MessageType == "revocation")
                {
                    if (envelope.Payload?.Subscription?.Type == EventSubProtocol.RedemptionType ||
                        envelope.Payload?.Subscription?.Type == EventSubProtocol.RedemptionUpdateType)
                    { _redemptionStatus = "Revoked"; continue; }
                    _log("[CS2TwitchCitizens] Twitch chat subscription revoked");
                    throw new AuthenticationException("Twitch subscription revoked.");
                }

                if (envelope.Metadata?.MessageType != "notification")
                    continue;

                if (cancellationToken.IsCancellationRequested || !_messageIds.TryAdd(envelope.Metadata.MessageId)) continue;
                if (_redemptions != null && EventSubProtocol.TryGetRedemption(envelope, _config.BroadcasterUserId,
                    DateTimeOffset.UtcNow, out var redemption))
                {
                    _log("[Interactive] source=EventSub result=received");
                    if (_rewardIcons.TryGetValue(redemption!.RewardId, out var icon)) redemption.RewardIconUrl = icon;
                    if (!_redemptions.TryEnqueue(redemption!))
                        _log("[Interactive] stage=Enqueue result=QueueFull");
                    else _log("[Interactive] stage=Enqueue result=accepted");
                }
                else if (envelope.Payload?.Subscription?.Type is EventSubProtocol.RedemptionType or
                    EventSubProtocol.RedemptionUpdateType)
                    _log("[Interactive] source=EventSub result=InvalidEvent");
                else if (EventSubProtocol.TryGetCommand(envelope, _config.BroadcasterUserId, DateTimeOffset.UtcNow, out var command))
                {
                    _queue.Enqueue(command!);
                    _log($"[CS2TwitchCitizens] TWITCH COMMAND viewer={command!.TwitchUserId} login={command.Login} command={command.Command}");
                }
            }
        }
        finally
        {
            lock (_socketGate)
            {
                if (ReferenceEquals(_socket, socket)) _socket = null;
            }
            socket.Dispose();
        }
    }

    private async Task<ClientWebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
            }
            lock (_socketGate)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                _socket = socket;
            }
            _log("[CS2TwitchCitizens] Twitch connected");
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task SubscribeAsync(string sessionId, CancellationToken cancellationToken)
    {
        await PostSubscriptionAsync("channel.chat.message", _config.UserId, sessionId, cancellationToken).ConfigureAwait(false);
        _log($"[CS2TwitchCitizens] Twitch chat subscription active broadcaster={_config.BroadcasterUserId}");
    }

    private async Task SubscribeRedemptionsAsync(string sessionId, CancellationToken cancellationToken)
    {
        await PostSubscriptionAsync(EventSubProtocol.RedemptionType, null, sessionId, cancellationToken).ConfigureAwait(false);
        _log("[CS2TwitchCitizens] Channel Points subscription active");
    }

    private async Task SubscribeRedemptionUpdatesAsync(string sessionId, CancellationToken cancellationToken)
    {
        await PostSubscriptionAsync(EventSubProtocol.RedemptionUpdateType, null, sessionId, cancellationToken).ConfigureAwait(false);
    }

    public static string BuildSubscriptionJson(string type, string broadcasterId, string? userId, string sessionId)
    {
        if (type != "channel.chat.message" && type != EventSubProtocol.RedemptionType &&
            type != EventSubProtocol.RedemptionUpdateType) throw new ArgumentOutOfRangeException(nameof(type));
        if (string.IsNullOrWhiteSpace(broadcasterId) || string.IsNullOrWhiteSpace(sessionId) ||
            (type == "channel.chat.message" ? string.IsNullOrWhiteSpace(userId) : userId != null))
            throw new ArgumentException("Invalid EventSub subscription condition.");
        var subscription = new SubscriptionRequest { Type = type, Version = "1",
            Condition = new SubscriptionCondition { BroadcasterUserId = broadcasterId, UserId = userId },
            Transport = new SubscriptionTransport { Method = "websocket", SessionId = sessionId } };
        using (var stream = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(SubscriptionRequest)).WriteObject(stream, subscription);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private async Task PostSubscriptionAsync(string type, string? userId, string sessionId, CancellationToken cancellationToken)
    {
        var body = BuildSubscriptionJson(type, _config.BroadcasterUserId, userId, sessionId);
        using (var request = new HttpRequestMessage(HttpMethod.Post, SubscriptionsEndpoint))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AccessToken);
            request.Headers.Add("Client-Id", _config.ClientId);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using (var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new AuthenticationException("EventSub subscription unauthorized.");
                if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.BadRequest)
                    throw new UnauthorizedAccessException("EventSub subscription rejected.");
                if (response.StatusCode != HttpStatusCode.Accepted)
                {
                    _log($"[CS2TwitchCitizens] Twitch subscription failed HTTP {(int)response.StatusCode} type={type}");
                    throw new HttpRequestException("EventSub subscription failed.");
                }
            }
        }
    }

    private static TimeSpan KeepaliveTimeout(EventSubEnvelope welcome) =>
        TimeSpan.FromSeconds(Math.Max(20, Math.Min(610, (welcome.Payload?.Session?.KeepaliveTimeoutSeconds ?? 10) + 10)));

    private static async Task<EventSubEnvelope?> ReadAsync(ClientWebSocket socket, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using (var timeoutToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        using (var data = new MemoryStream())
        {
            timeoutToken.CancelAfter(timeout);
            var buffer = new byte[8192];
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeoutToken.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Unexpected Twitch frame.");
                data.Write(buffer, 0, result.Count);
                if (data.Length > 131072) throw new InvalidDataException("Twitch frame too large.");
            } while (!result.EndOfMessage);

            EventSubProtocol.TryRead(Encoding.UTF8.GetString(data.ToArray()), out var envelope);
            return envelope;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _status = TwitchConnectionStatus.Disconnected;
        _redemptionStatus = "Disconnected";
        _stop.Cancel();
        lock (_socketGate)
        {
            try { _socket?.Abort(); } catch (WebSocketException) { }
            _socket = null;
        }
        _http.Dispose();
        if (_worker == null)
            _stop.Dispose();
        else
            _worker.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }

    [DataContract]
    private sealed class TokenValidation
    {
        [DataMember(Name = "client_id")] public string? ClientId { get; set; }
        [DataMember(Name = "user_id")] public string? UserId { get; set; }
        [DataMember(Name = "scopes")] public string[]? Scopes { get; set; }
    }

    [DataContract]
    private sealed class SubscriptionRequest
    {
        [DataMember(Name = "type")] public string? Type { get; set; }
        [DataMember(Name = "version")] public string? Version { get; set; }
        [DataMember(Name = "condition")] public SubscriptionCondition? Condition { get; set; }
        [DataMember(Name = "transport")] public SubscriptionTransport? Transport { get; set; }
    }

    [DataContract]
    private sealed class SubscriptionCondition
    {
        [DataMember(Name = "broadcaster_user_id")] public string? BroadcasterUserId { get; set; }
        [DataMember(Name = "user_id", EmitDefaultValue = false)] public string? UserId { get; set; }
    }

    [DataContract]
    private sealed class SubscriptionTransport
    {
        [DataMember(Name = "method")] public string? Method { get; set; }
        [DataMember(Name = "session_id")] public string? SessionId { get; set; }
    }
}
