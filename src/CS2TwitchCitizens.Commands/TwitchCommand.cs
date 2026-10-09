namespace CS2TwitchCitizens.Commands;

/// <summary>A parsed chat command. TwitchUserId is the viewer's identity; names are metadata.</summary>
public sealed class TwitchCommand
{
    public TwitchCommand(
        string twitchUserId,
        string login,
        string displayName,
        string command,
        string arguments,
        DateTimeOffset receivedAt,
        string messageId = "",
        bool isModerator = false,
        bool isBroadcaster = false)
    {
        if (string.IsNullOrWhiteSpace(twitchUserId))
            throw new ArgumentException("A Twitch user ID is required.", nameof(twitchUserId));

        TwitchUserId = twitchUserId;
        Login = login ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        Command = command ?? throw new ArgumentNullException(nameof(command));
        Arguments = arguments ?? string.Empty;
        ReceivedAt = receivedAt;
        MessageId = messageId ?? string.Empty;
        IsModerator = isModerator;
        IsBroadcaster = isBroadcaster;
    }

    public string TwitchUserId { get; }
    public string Login { get; }
    public string DisplayName { get; }
    public string Command { get; }
    public string Arguments { get; }
    public DateTimeOffset ReceivedAt { get; }
    public string MessageId { get; }
    public bool IsModerator { get; }
    public bool IsBroadcaster { get; }
}
