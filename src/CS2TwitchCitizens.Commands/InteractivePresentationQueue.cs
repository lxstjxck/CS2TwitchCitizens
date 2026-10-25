namespace CS2TwitchCitizens.Commands;

/// <summary>Game-thread presentation order; never changes Twitch entities or calls the network.</summary>
public sealed class InteractivePresentationQueue
{
    private readonly Queue<CityEventRecord> _pending = new();
    public CityEventRecord? Active { get; private set; }
    public DateTimeOffset ActiveUntil { get; private set; }
    public int PendingCount => _pending.Count;

    public bool TryEnqueue(CityEventRecord record)
    {
        if (_pending.Count >= 16) return false;
        record.State = "Queued";
        record.Result = "Waiting for display";
        _pending.Enqueue(record);
        return true;
    }

    public bool NeedsAdvance(DateTimeOffset now) =>
        Active != null && now >= ActiveUntil || Active == null && _pending.Count > 0;

    public CityEventRecord? Advance(DateTimeOffset now)
    {
        var previous = Active;
        Active = _pending.Count > 0 ? _pending.Dequeue() : null;
        if (Active != null)
        {
            Active.State = "AwaitingDisplay";
            Active.Result = "Waiting for UI acknowledgement";
            ActiveUntil = now.AddSeconds(30);
        }
        return previous;
    }

    public bool Acknowledge(string id, DateTimeOffset now)
    {
        if (Active == null || Active.Id != id || Active.State != "AwaitingDisplay") return false;
        Active.State = "Completed";
        Active.Result = "UI layout verified; no simulation change";
        ActiveUntil = now.AddSeconds(Active.Type == InteractiveAction.CityAnnouncement ? 30 : 8);
        return true;
    }

    public void RemoveRedemption(string redemptionId)
    {
        if (Active?.RedemptionId == redemptionId && Active.State == "AwaitingDisplay") Active = null;
        if (_pending.Count == 0) return;
        var keep = _pending.Where(record => record.RedemptionId != redemptionId).ToArray();
        _pending.Clear();
        foreach (var record in keep) _pending.Enqueue(record);
    }

    public CityEventRecord[] Reset()
    {
        var records = _pending.Concat(Active == null ? Array.Empty<CityEventRecord>() : new[] { Active }).ToArray();
        _pending.Clear();
        Active = null;
        return records;
    }
}
