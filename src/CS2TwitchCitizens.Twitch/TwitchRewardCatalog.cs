using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CS2TwitchCitizens.Twitch;

/// <summary>Optional read-only reward imagery. Never exposes OAuth tokens to UI.</summary>
public static class TwitchRewardCatalog
{
    public static async Task<Dictionary<string, string>> LoadAsync(HttpClient http, string clientId,
        string accessToken, string broadcasterId, CancellationToken cancellation)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var url = "https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id=" +
            Uri.EscapeDataString(broadcasterId);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", clientId);
        using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return result;
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var data = (RewardList?)new DataContractJsonSerializer(typeof(RewardList)).ReadObject(stream);
        foreach (var reward in data?.Data ?? Array.Empty<RewardInfo>())
        {
            if (string.IsNullOrWhiteSpace(reward.Id)) continue;
            var image = SafeIcon(reward.Image?.Url1x) ?? SafeIcon(reward.DefaultImage?.Url1x);
            if (image != null) result[reward.Id!] = image;
        }
        return result;
    }

    public static string? SafeIcon(string? value)
    {
        if (value == null || value.Length > 512 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host != "static-cdn.jtvnw.net" || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        return uri.AbsoluteUri;
    }

    [DataContract] private sealed class RewardList
    { [DataMember(Name = "data")] public RewardInfo[]? Data { get; set; } }
    [DataContract] private sealed class RewardInfo
    {
        [DataMember(Name = "id")] public string? Id { get; set; }
        [DataMember(Name = "image")] public RewardImage? Image { get; set; }
        [DataMember(Name = "default_image")] public RewardImage? DefaultImage { get; set; }
    }
    [DataContract] private sealed class RewardImage
    { [DataMember(Name = "url_1x")] public string? Url1x { get; set; } }
}
