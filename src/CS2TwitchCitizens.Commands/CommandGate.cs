namespace CS2TwitchCitizens.Commands;

/// <summary>Game-thread admission. A cooldown begins when an enabled, permitted command is handled.</summary>
public sealed class CommandGate
{
    private readonly Dictionary<(string Viewer, string Verb), DateTimeOffset> _perCommand = new();
    private readonly Dictionary<string, DateTimeOffset> _shared = new(StringComparer.Ordinal);
    private int _admissions;
    public void Clear() { _perCommand.Clear(); _shared.Clear(); }
    public void ForgetViewer(string viewerId)
    {
        _shared.Remove(viewerId);
        foreach (var key in _perCommand.Keys.Where(key => key.Viewer == viewerId).ToArray())
            _perCommand.Remove(key);
    }
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
        if (++_admissions % 256 == 0)
        {
            // Settings cap every cooldown at one hour. Prune only on this thread.
            var cutoff = command.ReceivedAt.AddHours(-1);
            foreach (var old in _perCommand.Where(pair => pair.Value < cutoff).Select(pair => pair.Key).ToArray())
                _perCommand.Remove(old);
            foreach (var old in _shared.Where(pair => pair.Value < cutoff).Select(pair => pair.Key).ToArray())
                _shared.Remove(old);
        }
        return true;
    }
}
