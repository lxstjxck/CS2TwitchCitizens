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
        if (message is null || twitchUserId is null)
            return false;
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(twitchUserId))
            return false;

        var input = message.Trim();
        var separator = input.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        var verb = separator < 0 ? input : input.Substring(0, separator);
        if (!verb.Equals("!join", StringComparison.OrdinalIgnoreCase) &&
            !verb.Equals("!me", StringComparison.OrdinalIgnoreCase) &&
            !verb.Equals("!find", StringComparison.OrdinalIgnoreCase) &&
            !verb.Equals("!history", StringComparison.OrdinalIgnoreCase))
            return false;

        var arguments = separator < 0 ? string.Empty : input.Substring(separator + 1).Trim();
        command = new TwitchCommand(
            twitchUserId, login ?? string.Empty, displayName ?? string.Empty,
            verb.ToLowerInvariant(), arguments, receivedAt);
        return true;
    }
}
