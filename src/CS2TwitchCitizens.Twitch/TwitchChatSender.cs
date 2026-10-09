using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CS2TwitchCitizens.Twitch;

/// <summary>HTTP only. The caller provides immutable text and an optional EventSub message ID.</summary>
public sealed class TwitchChatSender : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    public TwitchChatSender(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _ownsHttp = http == null;
    }
    public async Task<ChatSendResult> SendAsync(string clientId, string accessToken, string broadcasterId,
        string senderId, string message, string replyParentId, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 500) throw new ArgumentOutOfRangeException(nameof(message));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitch.tv/helix/chat/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", clientId);
        var body = new ChatSendBody { BroadcasterId = broadcasterId, SenderId = senderId,
            Message = message, ReplyParentMessageId = string.IsNullOrWhiteSpace(replyParentId) ? null : replyParentId };
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(ChatSendBody)).WriteObject(stream, body);
        request.Content = new StringContent(Encoding.UTF8.GetString(stream.ToArray()), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return ChatSendResult.Unauthorized;
        if (response.StatusCode == HttpStatusCode.Forbidden) return ChatSendResult.Forbidden;
        if ((int)response.StatusCode == 429) return ChatSendResult.RateLimited;
        if (!response.IsSuccessStatusCode) return ChatSendResult.Failed;
        using var answer = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var result = (ChatSendResponse?)new DataContractJsonSerializer(typeof(ChatSendResponse)).ReadObject(answer);
        return result?.Data?.Length > 0 && result.Data[0].IsSent ? ChatSendResult.Sent : ChatSendResult.Dropped;
    }
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}

public enum ChatSendResult { Sent, Dropped, Unauthorized, Forbidden, RateLimited, Failed }

[DataContract]
internal sealed class ChatSendBody
{
    [DataMember(Name = "broadcaster_id")] public string BroadcasterId { get; set; } = "";
    [DataMember(Name = "sender_id")] public string SenderId { get; set; } = "";
    [DataMember(Name = "message")] public string Message { get; set; } = "";
    [DataMember(Name = "reply_parent_message_id", EmitDefaultValue = false)] public string? ReplyParentMessageId { get; set; }
}

[DataContract]
internal sealed class ChatSendResponse
{
    [DataMember(Name = "data")] public ChatSendItem[]? Data { get; set; }
}

[DataContract]
internal sealed class ChatSendItem
{
    [DataMember(Name = "is_sent")] public bool IsSent { get; set; }
}
