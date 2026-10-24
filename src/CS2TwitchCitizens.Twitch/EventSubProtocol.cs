using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using CS2TwitchCitizens.Commands;

namespace CS2TwitchCitizens.Twitch;

[DataContract]
public sealed class EventSubEnvelope
{
    [DataMember(Name = "metadata")] public EventSubMetadata? Metadata { get; set; }
    [DataMember(Name = "payload")] public EventSubPayload? Payload { get; set; }
}

[DataContract]
public sealed class EventSubMetadata
{
    [DataMember(Name = "message_id")] public string? MessageId { get; set; }
    [DataMember(Name = "message_type")] public string? MessageType { get; set; }
}

[DataContract]
public sealed class EventSubPayload
{
    [DataMember(Name = "session")] public EventSubSession? Session { get; set; }
    [DataMember(Name = "subscription")] public EventSubSubscription? Subscription { get; set; }
    [DataMember(Name = "event")] public EventSubChatEvent? Event { get; set; }
}

[DataContract]
public sealed class EventSubSession
{
    [DataMember(Name = "id")] public string? Id { get; set; }
    [DataMember(Name = "keepalive_timeout_seconds")] public int? KeepaliveTimeoutSeconds { get; set; }
    [DataMember(Name = "reconnect_url")] public string? ReconnectUrl { get; set; }
}

[DataContract]
public sealed class EventSubSubscription
{
    [DataMember(Name = "type")] public string? Type { get; set; }
}

[DataContract]
public sealed class EventSubChatEvent
{
    [DataMember(Name = "id")] public string? Id { get; set; }
    [DataMember(Name = "broadcaster_user_id")] public string? BroadcasterUserId { get; set; }
    [DataMember(Name = "user_id")] public string? UserId { get; set; }
    [DataMember(Name = "user_name")] public string? UserName { get; set; }
    [DataMember(Name = "user_input")] public string? UserInput { get; set; }
    [DataMember(Name = "status")] public string? Status { get; set; }
    [DataMember(Name = "reward")] public EventSubReward? Reward { get; set; }
    [DataMember(Name = "chatter_user_id")] public string? ChatterUserId { get; set; }
    [DataMember(Name = "chatter_user_login")] public string? ChatterUserLogin { get; set; }
    [DataMember(Name = "chatter_user_name")] public string? ChatterUserName { get; set; }
    [DataMember(Name = "message_id")] public string? MessageId { get; set; }
    [DataMember(Name = "badges")] public EventSubBadge[]? Badges { get; set; }
    [DataMember(Name = "message")] public EventSubChatMessage? Message { get; set; }
}

[DataContract]
public sealed class EventSubReward
{
    [DataMember(Name = "id")] public string? Id { get; set; }
}

[DataContract]
public sealed class EventSubBadge
{
    [DataMember(Name = "set_id")] public string? SetId { get; set; }
}

[DataContract]
public sealed class EventSubChatMessage
{
    [DataMember(Name = "text")] public string? Text { get; set; }
}

public static class EventSubProtocol
{
    public const string RedemptionType = "channel.channel_points_custom_reward_redemption.add";
    public const string RedemptionUpdateType = "channel.channel_points_custom_reward_redemption.update";

    public static bool TryGetRedemption(EventSubEnvelope envelope, string broadcasterUserId,
        DateTimeOffset receivedAt, out RedemptionEvent? redemption)
    {
        redemption = null;
        var data = envelope.Payload?.Event;
        var type = envelope.Payload?.Subscription?.Type;
        if (envelope.Metadata?.MessageType != "notification" ||
            (type != RedemptionType && type != RedemptionUpdateType) ||
            data?.BroadcasterUserId != broadcasterUserId ||
            string.IsNullOrWhiteSpace(data.Id) || string.IsNullOrWhiteSpace(data.Reward?.Id) ||
            string.IsNullOrWhiteSpace(data.UserId) ||
            (type == RedemptionType && data.Status != "unfulfilled" && data.Status != "fulfilled") ||
            (type == RedemptionUpdateType && data.Status != "canceled" && data.Status != "fulfilled") ||
            data.UserInput?.Length > 4096)
            return false;
        redemption = new RedemptionEvent { RedemptionId = data.Id!, RewardId = data.Reward!.Id!,
            BroadcasterId = data.BroadcasterUserId!, TwitchUserId = data.UserId!,
            DisplayName = data.UserName ?? "", UserInput = data.UserInput ?? "",
            Status = data.Status!, IsUpdate = type == RedemptionUpdateType, ReceivedAt = receivedAt };
        return true;
    }
    public static bool TryGetReconnectUri(EventSubEnvelope envelope, out Uri? uri)
    {
        uri = null;
        if (envelope.Metadata?.MessageType != "session_reconnect" ||
            !Uri.TryCreate(envelope.Payload?.Session?.ReconnectUrl, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != "wss" || parsed.Host != "eventsub.wss.twitch.tv")
            return false;
        uri = parsed;
        return true;
    }

    public static bool TryRead(string json, out EventSubEnvelope? envelope)
    {
        envelope = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 131072)
            return false;
        try
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                envelope = (EventSubEnvelope?)new DataContractJsonSerializer(typeof(EventSubEnvelope)).ReadObject(stream);
            return envelope?.Metadata?.MessageType != null;
        }
        catch (Exception ex) when (ex is SerializationException || ex is FormatException || ex is ArgumentException || ex is System.Xml.XmlException)
        {
            return false;
        }
    }

    public static bool TryGetCommand(EventSubEnvelope envelope, string broadcasterUserId, DateTimeOffset receivedAt, out TwitchCommand? command)
    {
        command = null;
        var eventData = envelope.Payload?.Event;
        if (envelope.Metadata?.MessageType != "notification" ||
            envelope.Payload?.Subscription?.Type != "channel.chat.message" ||
            eventData?.BroadcasterUserId != broadcasterUserId ||
            string.IsNullOrWhiteSpace(eventData.ChatterUserId))
            return false;

        if (!TwitchCommandParser.TryParse(
            eventData.Message?.Text,
            eventData.ChatterUserId,
            eventData.ChatterUserLogin,
            eventData.ChatterUserName,
            receivedAt,
            out command,
            eventData.MessageId ?? string.Empty,
            eventData.Badges?.Any(b => b.SetId == "moderator" || b.SetId == "broadcaster") == true,
            eventData.ChatterUserId == broadcasterUserId) || (command?.Command != "!join" && command?.Command != "!me" && command?.Command != "!find" && command?.Command != "!history"))
        {
            command = null;
            return false;
        }
        return true;
    }
}
