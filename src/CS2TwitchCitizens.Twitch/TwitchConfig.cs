using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CS2TwitchCitizens.Twitch;

[DataContract]
public sealed class TwitchConfig
{
    [DataMember(Name = "enabled")] public bool Enabled { get; set; }
    [DataMember(Name = "clientId")] public string ClientId { get; set; } = string.Empty;
    [DataMember(Name = "accessToken")] public string AccessToken { get; set; } = string.Empty;
    [DataMember(Name = "broadcasterUserId")] public string BroadcasterUserId { get; set; } = string.Empty;
    [DataMember(Name = "userId")] public string UserId { get; set; } = string.Empty;

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(BroadcasterUserId) &&
        !string.IsNullOrWhiteSpace(UserId);

    public static TwitchConfig Load(string path)
    {
        using (var stream = File.OpenRead(path))
            return (TwitchConfig?)new DataContractJsonSerializer(typeof(TwitchConfig)).ReadObject(stream)
                ?? throw new SerializationException("Twitch config is empty.");
    }
}
