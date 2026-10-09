using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CS2TwitchCitizens.Twitch;

public sealed class TwitchOAuthClient : IDisposable
{
    private const string Base = "https://id.twitch.tv/oauth2/";
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    public TwitchOAuthClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _ownsHttp = http == null;
    }

    public async Task<TwitchDeviceCode> RequestDeviceAsync(string clientId, CancellationToken cancellation)
    {
        using var response = await PostAsync("device", new Dictionary<string, string> {
            ["client_id"] = clientId, ["scopes"] = "user:read:chat user:write:chat"
        }, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Device authorization unavailable.");
        var code = await ReadAsync<TwitchDeviceCode>(response).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(code.DeviceCode) || string.IsNullOrWhiteSpace(code.UserCode) ||
            !Uri.TryCreate(code.VerificationUri, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "www.twitch.tv" || code.ExpiresIn <= 0 || code.Interval <= 0)
            throw new InvalidDataException("Invalid Twitch device authorization response.");
        return code;
    }

    public async Task<TwitchToken?> PollAsync(string clientId, string deviceCode, CancellationToken cancellation)
    {
        using var response = await PostAsync("token", new Dictionary<string, string> {
            ["client_id"] = clientId, ["scopes"] = "user:read:chat user:write:chat", ["device_code"] = deviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
        }, cancellation).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return await ReadAsync<TwitchToken>(response).ConfigureAwait(false);
        if (response.StatusCode == (HttpStatusCode)429) throw new TwitchRateLimitException();
        var error = await ReadAsync<TwitchError>(response).ConfigureAwait(false);
        if (error.Message == "authorization_pending") return null;
        if (error.Message == "slow_down") throw new TwitchRateLimitException();
        if (error.Message == "access_denied") throw new UnauthorizedAccessException("Authorization denied.");
        if (error.Message == "expired_token" || error.Message == "invalid device code") throw new TimeoutException("Authorization code expired.");
        throw new HttpRequestException("Device authorization failed.");
    }

    public async Task<TwitchToken> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellation)
    {
        using var response = await PostAsync("token", new Dictionary<string, string> {
            ["client_id"] = clientId, ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token"
        }, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Twitch access revoked.");
        response.EnsureSuccessStatusCode();
        return await ReadAsync<TwitchToken>(response).ConfigureAwait(false);
    }

    public async Task<TwitchIdentity> ValidateAsync(string clientId, string token, CancellationToken cancellation,
        bool requireWrite = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Base + "validate");
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
        using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException("Twitch access revoked.");
        response.EnsureSuccessStatusCode();
        var identity = await ReadAsync<TwitchIdentity>(response).ConfigureAwait(false);
        if (identity.ClientId != clientId || string.IsNullOrWhiteSpace(identity.UserId) ||
            identity.Scopes == null || !identity.Scopes.Contains("user:read:chat") ||
            (requireWrite && !identity.Scopes.Contains("user:write:chat")))
            throw new UnauthorizedAccessException("Twitch account or scope mismatch.");
        return identity;
    }

    public async Task<string> GetDisplayNameAsync(string clientId, string token, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Client-Id", clientId);
            using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return "";
            var users = await ReadAsync<TwitchUsers>(response).ConfigureAwait(false);
            return users.Data?.FirstOrDefault()?.DisplayName ?? "";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch { return ""; }
    }

    public async Task RevokeAsync(string clientId, string token, CancellationToken cancellation)
    {
        using var response = await PostAsync("revoke", new Dictionary<string, string> {
            ["client_id"] = clientId, ["token"] = token
        }, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> PostAsync(string endpoint, Dictionary<string, string> fields, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Base + endpoint) { Content = new FormUrlEncodedContent(fields) };
        return await _http.SendAsync(request, cancellation).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return (T?)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)
            ?? throw new InvalidDataException("Empty Twitch response.");
    }
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}

public sealed class TwitchRateLimitException : Exception { }

[DataContract]
public sealed class TwitchDeviceCode
{
    [DataMember(Name = "device_code")] public string DeviceCode { get; set; } = "";
    [DataMember(Name = "user_code")] public string UserCode { get; set; } = "";
    [DataMember(Name = "verification_uri")] public string VerificationUri { get; set; } = "";
    [DataMember(Name = "expires_in")] public int ExpiresIn { get; set; }
    [DataMember(Name = "interval")] public int Interval { get; set; }
}

[DataContract]
public sealed class TwitchToken
{
    [DataMember(Name = "access_token")] public string AccessToken { get; set; } = "";
    [DataMember(Name = "refresh_token")] public string RefreshToken { get; set; } = "";
    [DataMember(Name = "expires_in")] public int ExpiresIn { get; set; }
}

[DataContract]
public sealed class TwitchIdentity
{
    [DataMember(Name = "client_id")] public string ClientId { get; set; } = "";
    [DataMember(Name = "user_id")] public string UserId { get; set; } = "";
    [DataMember(Name = "login")] public string Login { get; set; } = "";
    [DataMember(Name = "scopes")] public string[] Scopes { get; set; } = Array.Empty<string>();
}

[DataContract]
internal sealed class TwitchError
{
    [DataMember(Name = "message")] public string Message { get; set; } = "";
}

[DataContract]
internal sealed class TwitchUsers
{
    [DataMember(Name = "data")] public TwitchUser[]? Data { get; set; }
}

[DataContract]
internal sealed class TwitchUser
{
    [DataMember(Name = "display_name")] public string DisplayName { get; set; } = "";
}
