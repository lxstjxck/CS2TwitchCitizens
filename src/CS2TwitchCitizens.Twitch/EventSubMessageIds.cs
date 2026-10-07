namespace CS2TwitchCitizens.Twitch;

/// <summary>Bounded in-memory deduplication for Twitch's at-least-once notifications.</summary>
public sealed class EventSubMessageIds
{
    private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
    private readonly Queue<string> _order = new Queue<string>();
    private readonly int _capacity;

    public EventSubMessageIds(int capacity = 4096)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool TryAdd(string? id)
    {
        if (id is null || string.IsNullOrWhiteSpace(id) || !_seen.Add(id))
            return false;
        _order.Enqueue(id);
        if (_order.Count > _capacity)
            _seen.Remove(_order.Dequeue());
        return true;
    }
}
