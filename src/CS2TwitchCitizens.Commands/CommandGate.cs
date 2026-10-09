namespace CS2TwitchCitizens.Commands;

/// <summary>Game-thread admission. A cooldown begins when an enabled, permitted command is handled.</summary>
public sealed class CommandGate
{
    private readonly Dictionary<(string Viewer, string Verb), DateTimeOffset> _perCommand = new();
    private readonly Dictionary<string, DateTimeOffset> _shared = new(StringComparer.Ordinal);
    public void Clear() { _perCommand.Clear(); _shared.Clear(); }
    public bool TryAdmit(TwitchCommand command, CommandSettings settings)
    {
        var options = settings.For(command.Command);
        if (!options.Enabled ||
            options.Permission == "broadcaster" && !command.IsBroadcaster ||
            options.Permission == "moderators" && !command.IsModerator && !command.IsBroadcaster)
            return false;
        var key = (command.TwitchUserId, command.Command);
        if (_perCommand.TryGetValue(key, out var last) &&
            command.ReceivedAt < last.AddSeconds(options.ViewerCooldownSeconds)) return false;
        if (_shared.TryGetValue(command.TwitchUserId, out last) &&
            command.ReceivedAt < last.AddSeconds(settings.SharedCooldownSeconds)) return false;
        _perCommand[key] = command.ReceivedAt;
        _shared[command.TwitchUserId] = command.ReceivedAt;
        return true;
    }
}
