using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CS2TwitchCitizens.Twitch;

/// <summary>Helix-only reward operations. Caller supplies a validated broadcaster token.</summary>
public sealed class TwitchRewardManager : IDisposable
{
    private const string Endpoint = "https://api.twitch.tv/helix/channel_points/custom_rewards";
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    public TwitchRewardManager(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _ownsHttp = http == null;
    }

    public async Task<TwitchReward[]> ListAsync(string clientId, string token, string broadcasterId,
        bool manageableOnly, CancellationToken cancellation)
    {
        var uri = RewardUri(broadcasterId) + (manageableOnly ? "&only_manageable_rewards=true" : "");
        var data = await SendAsync<TwitchRewardList>(HttpMethod.Get, uri, null, clientId, token, cancellation, true);
        return data.Data ?? Array.Empty<TwitchReward>();
    }

    public async Task<TwitchReward> CreateAsync(string clientId, string token, string broadcasterId,
        TwitchRewardEdit edit, CancellationToken cancellation)
    {
        edit.Validate(true);
        var data = await SendAsync<TwitchRewardList>(HttpMethod.Post, RewardUri(broadcasterId), edit,
            clientId, token, cancellation, false);
        return Single(data.Data);
    }

    public async Task<TwitchReward> UpdateAsync(string clientId, string token, string broadcasterId,
        string rewardId, TwitchRewardEdit edit, CancellationToken cancellation)
    {
        RequireId(rewardId);
        edit.Validate(false);
        var data = await SendAsync<TwitchRewardList>(new HttpMethod("PATCH"), RewardUri(broadcasterId) +
            "&id=" + Uri.EscapeDataString(rewardId), edit, clientId, token, cancellation, false);
        return Single(data.Data);
    }

    public async Task DeleteAsync(string clientId, string token, string broadcasterId, string rewardId,
        CancellationToken cancellation)
    {
        RequireId(rewardId);
        await SendAsync<object>(HttpMethod.Delete, RewardUri(broadcasterId) + "&id=" +
            Uri.EscapeDataString(rewardId), null, clientId, token, cancellation, false, true);
    }

    public async Task<TwitchRedemption[]> ListRedemptionsAsync(string clientId, string token,
        string broadcasterId, string rewardId, string status, CancellationToken cancellation)
    {
        RequireId(rewardId);
        if (status is not ("UNFULFILLED" or "FULFILLED" or "CANCELED"))
            throw new ArgumentException("Invalid redemption status.", nameof(status));
        var uri = RedemptionUri(broadcasterId, rewardId) + "&status=" + status + "&first=50";
        var result = new List<TwitchRedemption>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var cursor = "";
        for (var page = 0; page < 100; page++)
        {
            var data = await SendAsync<TwitchRedemptionList>(HttpMethod.Get,
                uri + (cursor.Length == 0 ? "" : "&after=" + Uri.EscapeDataString(cursor)), null,
                clientId, token, cancellation, true);
            result.AddRange(data.Data ?? Array.Empty<TwitchRedemption>());
            cursor = data.Pagination?.Cursor ?? "";
            if (cursor.Length == 0) return result.ToArray();
            if (!cursors.Add(cursor)) throw new InvalidDataException("Repeated Twitch pagination cursor.");
        }
        throw new InvalidDataException("Too many Twitch redemption pages.");
    }

    public async Task<TwitchRedemption> GetRedemptionAsync(string clientId, string token,
        string broadcasterId, string rewardId, string redemptionId, CancellationToken cancellation)
    {
        RequireId(redemptionId);
        var data = await SendAsync<TwitchRedemptionList>(HttpMethod.Get,
            RedemptionUri(broadcasterId, rewardId) + "&id=" + Uri.EscapeDataString(redemptionId),
            null, clientId, token, cancellation, true);
        return Single(data.Data);
    }

    public async Task<TwitchRedemption> SetRedemptionStatusAsync(string clientId, string token,
        string broadcasterId, string rewardId, string redemptionId, string status, CancellationToken cancellation)
    {
        RequireId(redemptionId);
        if (status is not ("FULFILLED" or "CANCELED"))
            throw new ArgumentException("Invalid redemption status.", nameof(status));
        var body = new TwitchRedemptionStatus { Status = status };
        var data = await SendAsync<TwitchRedemptionList>(new HttpMethod("PATCH"),
            RedemptionUri(broadcasterId, rewardId) + "&id=" + Uri.EscapeDataString(redemptionId),
            body, clientId, token, cancellation, false);
        return Single(data.Data);
    }

    private static string RewardUri(string broadcasterId)
    {
        RequireId(broadcasterId);
        return Endpoint + "?broadcaster_id=" + Uri.EscapeDataString(broadcasterId);
    }
    private static string RedemptionUri(string broadcasterId, string rewardId)
    {
        RequireId(rewardId);
        return Endpoint + "/redemptions?broadcaster_id=" + Uri.EscapeDataString(broadcasterId) +
            "&reward_id=" + Uri.EscapeDataString(rewardId);
    }
    private static void RequireId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
            throw new ArgumentException("Invalid Twitch ID.");
    }
    private static T Single<T>(T[]? data) where T : class => data is { Length: 1 } && data[0] != null
        ? data[0] : throw new InvalidDataException("Unexpected Twitch response.");

    private async Task<T> SendAsync<T>(HttpMethod method, string uri, object? body, string clientId,
        string token, CancellationToken cancellation, bool retryRead, bool noContent = false)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(token))
            throw new UnauthorizedAccessException("Twitch authorization required.");
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Client-Id", clientId);
            if (body != null)
            {
                using var stream = new MemoryStream();
                new DataContractJsonSerializer(body.GetType()).WriteObject(stream, body);
                request.Content = new StringContent(Encoding.UTF8.GetString(stream.ToArray()), Encoding.UTF8, "application/json");
            }
            using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
            if ((response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500) &&
                retryRead && attempt == 0)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                if (delay > TimeSpan.FromSeconds(10)) delay = TimeSpan.FromSeconds(10);
                await Task.Delay(delay, cancellation).ConfigureAwait(false);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new TwitchRewardException((int)response.StatusCode);
            if (noContent) return default!;
            using var content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return (T?)new DataContractJsonSerializer(typeof(T)).ReadObject(content)
                ?? throw new InvalidDataException("Empty Twitch response.");
        }
    }
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}

public sealed class TwitchRewardException : Exception
{
    public int StatusCode { get; }
    public TwitchRewardException(int statusCode) : base("Twitch reward API returned HTTP " + statusCode + ".")
        => StatusCode = statusCode;
}

[DataContract]
public sealed class TwitchRewardEdit
{
    [DataMember(Name = "title")] public string Title { get; set; } = "";
    [DataMember(Name = "cost")] public long Cost { get; set; } = 1;
    [DataMember(Name = "prompt")] public string Prompt { get; set; } = "";
    [DataMember(Name = "background_color")] public string BackgroundColor { get; set; } = "#00E5CB";
    [DataMember(Name = "is_enabled")] public bool IsEnabled { get; set; } = true;
    [DataMember(Name = "is_paused")] public bool IsPaused { get; set; }
    [DataMember(Name = "is_user_input_required")] public bool IsUserInputRequired { get; set; }
    [DataMember(Name = "is_max_per_stream_enabled")] public bool IsMaxPerStreamEnabled { get; set; }
    [DataMember(Name = "max_per_stream")] public int MaxPerStream { get; set; } = 1;
    [DataMember(Name = "is_max_per_user_per_stream_enabled")] public bool IsMaxPerUserPerStreamEnabled { get; set; }
    [DataMember(Name = "max_per_user_per_stream")] public int MaxPerUserPerStream { get; set; } = 1;
    [DataMember(Name = "is_global_cooldown_enabled")] public bool IsGlobalCooldownEnabled { get; set; }
    [DataMember(Name = "global_cooldown_seconds")] public int GlobalCooldownSeconds { get; set; } = 1;
    [DataMember(Name = "should_redemptions_skip_request_queue")] public bool SkipQueue { get; set; }

    public void Validate(bool creating)
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 45 || Prompt == null || Prompt.Length > 200 || Cost < 1 ||
            BackgroundColor == null || BackgroundColor.Length != 7 || BackgroundColor[0] != '#' ||
            BackgroundColor.Skip(1).Any(c => !Uri.IsHexDigit(c)) ||
            IsMaxPerStreamEnabled && MaxPerStream < 1 ||
            IsMaxPerUserPerStreamEnabled && MaxPerUserPerStream < 1 ||
            IsGlobalCooldownEnabled && (GlobalCooldownSeconds < 1 || GlobalCooldownSeconds > 604800))
            throw new ArgumentException("Invalid reward fields.");
    }
}

[DataContract]
public sealed class TwitchReward
{
    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "title")] public string Title { get; set; } = "";
    [DataMember(Name = "prompt")] public string Prompt { get; set; } = "";
    [DataMember(Name = "cost")] public long Cost { get; set; }
    [DataMember(Name = "background_color")] public string BackgroundColor { get; set; } = "";
    [DataMember(Name = "is_enabled")] public bool IsEnabled { get; set; }
    [DataMember(Name = "is_paused")] public bool IsPaused { get; set; }
    [DataMember(Name = "is_user_input_required")] public bool IsUserInputRequired { get; set; }
    [DataMember(Name = "max_per_stream_setting")] public TwitchRewardLimit? MaxPerStream { get; set; }
    [DataMember(Name = "max_per_user_per_stream_setting")] public TwitchRewardLimit? MaxPerUserPerStream { get; set; }
    [DataMember(Name = "global_cooldown_setting")] public TwitchRewardCooldown? GlobalCooldown { get; set; }
    [DataMember(Name = "should_redemptions_skip_request_queue")] public bool SkipQueue { get; set; }
    [DataMember(Name = "image")] public TwitchRewardImage? Image { get; set; }
    [DataMember(Name = "default_image")] public TwitchRewardImage? DefaultImage { get; set; }
}
[DataContract] public sealed class TwitchRewardLimit
{
    [DataMember(Name = "is_enabled")] public bool IsEnabled { get; set; }
    [DataMember(Name = "max_per_stream")] public int MaxPerStream { get; set; }
    [DataMember(Name = "max_per_user_per_stream")] public int MaxPerUserPerStream { get; set; }
}
[DataContract] public sealed class TwitchRewardCooldown
{
    [DataMember(Name = "is_enabled")] public bool IsEnabled { get; set; }
    [DataMember(Name = "global_cooldown_seconds")] public int Seconds { get; set; }
}
[DataContract] public sealed class TwitchRewardImage
{ [DataMember(Name = "url_1x")] public string Url1x { get; set; } = ""; }
[DataContract] public sealed class TwitchRewardList
{ [DataMember(Name = "data")] public TwitchReward[]? Data { get; set; } }
[DataContract] public sealed class TwitchRedemption
{
    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "broadcaster_id")] public string BroadcasterId { get; set; } = "";
    [DataMember(Name = "user_id")] public string UserId { get; set; } = "";
    [DataMember(Name = "user_name")] public string UserName { get; set; } = "";
    [DataMember(Name = "status")] public string Status { get; set; } = "";
    [DataMember(Name = "user_input")] public string UserInput { get; set; } = "";
    [DataMember(Name = "reward")] public TwitchRedemptionReward? Reward { get; set; }
}
[DataContract] public sealed class TwitchRedemptionReward
{ [DataMember(Name = "id")] public string Id { get; set; } = ""; }
[DataContract] public sealed class TwitchRedemptionList
{
    [DataMember(Name = "data")] public TwitchRedemption[]? Data { get; set; }
    [DataMember(Name = "pagination")] public TwitchPagination? Pagination { get; set; }
}
[DataContract] public sealed class TwitchPagination
{ [DataMember(Name = "cursor")] public string? Cursor { get; set; } }
[DataContract] public sealed class TwitchRedemptionStatus
{ [DataMember(Name = "status")] public string Status { get; set; } = ""; }
