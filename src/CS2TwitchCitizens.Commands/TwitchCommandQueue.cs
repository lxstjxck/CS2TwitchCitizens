using System.Collections.Concurrent;

namespace CS2TwitchCitizens.Commands;

/// <summary>The only handoff needed between a future Twitch callback and game code.</summary>
public sealed class TwitchCommandQueue
{
    private readonly ConcurrentQueue<TwitchCommand> _commands = new();

    public void Enqueue(TwitchCommand command) =>
        _commands.Enqueue(command ?? throw new ArgumentNullException(nameof(command)));

    public bool TryDequeue(out TwitchCommand? command) => _commands.TryDequeue(out command);

    public int Count => _commands.Count;

    public void Clear()
    {
        while (_commands.TryDequeue(out _)) { }
    }
}
