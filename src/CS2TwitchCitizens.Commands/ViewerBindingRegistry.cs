using System.Collections.Generic;

namespace CS2TwitchCitizens.Commands;

public enum JoinResult
{
    Joined,
    AlreadyJoined,
    CitizenInUse
}

public enum BindingStatus
{
    NotJoined,
    Active,
    Stale
}

/// <summary>Session-only, framework-neutral binding rules. The caller owns key validation.</summary>
public sealed class ViewerBindingRegistry<TKey> where TKey : notnull
{
    private readonly Dictionary<string, CitizenBinding<TKey>> _byViewer =
        new Dictionary<string, CitizenBinding<TKey>>(StringComparer.Ordinal);
    private readonly HashSet<TKey> _claimedCitizens = new HashSet<TKey>();

    public bool IsCitizenBound(TKey citizenKey) => _claimedCitizens.Contains(citizenKey);

    public bool TryGet(string viewerId, out CitizenBinding<TKey>? binding) =>
        _byViewer.TryGetValue(viewerId, out binding);

    public IReadOnlyList<CitizenBinding<TKey>> GetAllBindings() =>
        new List<CitizenBinding<TKey>>(_byViewer.Values);

    public bool Remove(string viewerId)
    {
        if (!_byViewer.TryGetValue(viewerId, out var binding)) return false;
        _byViewer.Remove(viewerId);
        _claimedCitizens.Remove(binding.CitizenKey);
        return true;
    }

    public JoinResult Join(string viewerId, TKey citizenKey, out CitizenBinding<TKey> binding)
    {
        if (string.IsNullOrWhiteSpace(viewerId))
            throw new ArgumentException("A Twitch user ID is required.", nameof(viewerId));

        if (_byViewer.TryGetValue(viewerId, out binding!))
            return JoinResult.AlreadyJoined;

        if (_claimedCitizens.Contains(citizenKey))
        {
            binding = null!;
            return JoinResult.CitizenInUse;
        }

        binding = new CitizenBinding<TKey>(viewerId, citizenKey);
        _byViewer.Add(viewerId, binding);
        _claimedCitizens.Add(citizenKey);
        return JoinResult.Joined;
    }

    public BindingStatus GetStatus(
        string viewerId,
        Func<TKey, bool> citizenExists,
        out CitizenBinding<TKey>? binding)
    {
        if (citizenExists is null) throw new ArgumentNullException(nameof(citizenExists));
        if (!_byViewer.TryGetValue(viewerId, out binding))
            return BindingStatus.NotJoined;

        return citizenExists(binding.CitizenKey) ? BindingStatus.Active : BindingStatus.Stale;
    }
}
