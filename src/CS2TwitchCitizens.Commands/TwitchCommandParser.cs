namespace CS2TwitchCitizens.Commands;

public static class TwitchCommandParser
{
    public static bool TryParse(
        string? message,
        string? twitchUserId,
        string? login,
        string? displayName,
        DateTimeOffset receivedAt,
        out TwitchCommand? command)
    {
        command = null;
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(twitchUserId))
            return false;

        var input = message.Trim();
        var separator = input.IndexOfAny([' ', '\t', '\r', '\n']);
        var verb = separator < 0 ? input : input[..separator];
        if (!verb.Equals("!join", StringComparison.OrdinalIgnoreCase) &&
            !verb.Equals("!me", StringComparison.OrdinalIgnoreCase) &&
            !verb.Equals("!find", StringComparison.OrdinalIgnoreCase))
            return false;

        var arguments = separator < 0 ? string.Empty : input[(separator + 1)..].Trim();
        command = new TwitchCommand(
            twitchUserId, login ?? string.Empty, displayName ?? string.Empty,
            verb.ToLowerInvariant(), arguments, receivedAt);
        return true;
    }
}
