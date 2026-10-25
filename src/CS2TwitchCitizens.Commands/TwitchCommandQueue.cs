using System.Collections.Concurrent;

namespace CS2TwitchCitizens.Commands;

/// <summary>Bounded handoff from Twitch callbacks to the game thread.</summary>
public sealed class TwitchCommandQueue
{
    private readonly ConcurrentQueue<TwitchCommand> _commands = new();
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly TimeSpan _maximumAge;
    private readonly Func<DateTimeOffset> _clock;
    private long _rejected;
    private long _expired;
    private int _peak;
    private int _count;

    public TwitchCommandQueue(int capacity = 512, TimeSpan? maximumAge = null,
        Func<DateTimeOffset>? clock = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _maximumAge = maximumAge ?? TimeSpan.FromMinutes(2);
        if (_maximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumAge));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public int Count { get { lock (_gate) return _count; } }
    public long Rejected { get { lock (_gate) return _rejected; } }
    public long Expired { get { lock (_gate) return _expired; } }
    public int Peak { get { lock (_gate) return _peak; } }

    public void Enqueue(TwitchCommand command) => TryEnqueue(command);

    public bool TryEnqueue(TwitchCommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        lock (_gate)
        {
            var now = _clock();
            PruneExpired(now);
            if (IsExpired(command, now)) { _expired++; return false; }
            if (_count >= _capacity) { _rejected++; return false; }
            _commands.Enqueue(command);
            _count++;
            if (_count > _peak) _peak = _count;
            return true;
        }
    }

    public bool TryDequeue(out TwitchCommand? command)
    {
        lock (_gate)
        {
            PruneExpired(_clock());
            if (!_commands.TryDequeue(out command)) { command = null; return false; }
            _count--;
            return true;
        }
    }

    public void Clear() { lock (_gate) { while (_commands.TryDequeue(out _)) { } _count = 0; } }

    private void PruneExpired(DateTimeOffset now)
    {
        while (_commands.TryPeek(out var first) && IsExpired(first, now))
        { if (_commands.TryDequeue(out _)) { _count--; _expired++; } }
    }

    private bool IsExpired(TwitchCommand command, DateTimeOffset now) =>
        now - command.ReceivedAt > _maximumAge;
}
