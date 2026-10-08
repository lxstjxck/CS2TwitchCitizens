namespace CS2TwitchCitizens.Twitch;

public enum TwitchConnectionStatus
{
    Disabled,
    Connecting,
    Connected,
    Reconnecting,
    AuthenticationError,
    Disconnected
}
