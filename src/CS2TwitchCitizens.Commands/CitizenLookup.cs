namespace CS2TwitchCitizens.Commands;

public enum CitizenFindStatus
{
    NotJoined,
    Stale,
    PositionUnavailable,
    Found
}

public enum CitizenFocusStatus
{
    CitizenNotFound,
    PositionUnavailable,
    CameraUnavailable,
    FocusRequested,
    FocusConfirmed,
    FocusFailed,
    FollowRequested,
    FollowStopped
}

public readonly struct CitizenPosition
{
    public CitizenPosition(float x, float y, float z) { X = x; Y = y; Z = z; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public override string ToString() => $"({X}, {Y}, {Z})";
}

public sealed class CitizenFindResult<TKey> where TKey : notnull
{
    public CitizenFindResult(CitizenFindStatus status, TKey citizenKey, CitizenPosition? position)
    {
        Status = status;
        CitizenKey = citizenKey;
        Position = position;
    }
    public CitizenFindStatus Status { get; }
    public TKey CitizenKey { get; }
    public CitizenPosition? Position { get; }
}

/// <summary>Pure lookup policy. It never invokes camera controls or changes a binding.</summary>
public static class CitizenLookup
{
    public static CitizenFindResult<TKey> Find<TKey>(
        ViewerBindingRegistry<TKey> bindings,
        string viewerId,
        Func<TKey, bool> isValid,
        Func<TKey, CitizenPosition?> locate) where TKey : notnull
    {
        if (bindings is null) throw new ArgumentNullException(nameof(bindings));
        if (isValid is null) throw new ArgumentNullException(nameof(isValid));
        if (locate is null) throw new ArgumentNullException(nameof(locate));

        var status = bindings.GetStatus(viewerId, isValid, out var binding);
        if (status == BindingStatus.NotJoined)
            return new CitizenFindResult<TKey>(CitizenFindStatus.NotJoined, default!, null);
        if (status == BindingStatus.Stale)
            return new CitizenFindResult<TKey>(CitizenFindStatus.Stale, binding!.CitizenKey, null);

        var key = binding!.CitizenKey;
        var position = locate(key);
        return new CitizenFindResult<TKey>(
            position.HasValue ? CitizenFindStatus.Found : CitizenFindStatus.PositionUnavailable,
            key, position);
    }
}
