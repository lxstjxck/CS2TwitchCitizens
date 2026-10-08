using System;
using System.Collections.Generic;

namespace CS2TwitchCitizens.Commands;

public enum ViewerLifeStatus { Active, Deceased, Missing }

/// <summary>Plain city-save data. Completed lives retain their snapshot without an Entity key.</summary>
public sealed class ViewerLife<TKey> where TKey : notnull
{
    public string LifeId { get; set; } = string.Empty;
    public TKey CitizenKey { get; set; } = default!;
    public string OriginalCitizenName { get; set; } = string.Empty;
    public string TwitchDisplayName { get; set; } = string.Empty;
    public string StartGameDate { get; set; } = string.Empty;
    public string EndGameDate { get; set; } = string.Empty;
    public ViewerLifeStatus Status { get; set; }
    public string LastKnownAge { get; set; } = string.Empty;
    public string LastKnownWorkplace { get; set; } = string.Empty;
    public string LastKnownHome { get; set; } = string.Empty;
    public string CauseOfDeath { get; set; } = string.Empty;
}

public sealed class ViewerLifeAccount<TKey> where TKey : notnull
{
    public string TwitchUserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<ViewerLife<TKey>> Lives { get; set; } = new List<ViewerLife<TKey>>();
    public ViewerLife<TKey>? Current => Lives.Count == 0 ? null : Lives[Lives.Count - 1];
}

/// <summary>Game-thread life rules. Citizen keys are supplied by the game-facing caller.</summary>
public sealed class ViewerLifeJournal<TKey> where TKey : notnull
{
    private readonly Dictionary<string, ViewerLifeAccount<TKey>> _accounts =
        new Dictionary<string, ViewerLifeAccount<TKey>>(StringComparer.Ordinal);
    private readonly HashSet<TKey> _claimed = new HashSet<TKey>();

    public IReadOnlyCollection<ViewerLifeAccount<TKey>> Accounts => _accounts.Values;
    public bool IsClaimed(TKey key) => _claimed.Contains(key);
    public bool TryGet(string viewerId, out ViewerLifeAccount<TKey>? account) =>
        _accounts.TryGetValue(viewerId, out account);

    public bool TryStart(string viewerId, string login, string displayName, TKey key,
        string gameDate, string originalName, string age, string home, string workplace,
        out ViewerLife<TKey>? life)
    {
        life = null;
        if (string.IsNullOrWhiteSpace(viewerId) || _claimed.Contains(key)) return false;
        _accounts.TryGetValue(viewerId, out var account);
        if (account?.Current?.Status == ViewerLifeStatus.Active ||
            account?.Current?.Status == ViewerLifeStatus.Missing) return false;
        if (account == null)
        {
            account = new ViewerLifeAccount<TKey> { TwitchUserId = viewerId };
            _accounts.Add(viewerId, account);
        }
        account.Login = login;
        account.DisplayName = displayName;
        life = new ViewerLife<TKey> {
            LifeId = Guid.NewGuid().ToString("N"), CitizenKey = key,
            TwitchDisplayName = displayName, StartGameDate = gameDate,
            OriginalCitizenName = originalName, LastKnownAge = age,
            LastKnownHome = home, LastKnownWorkplace = workplace,
            Status = ViewerLifeStatus.Active
        };
        account.Lives.Add(life);
        _claimed.Add(key);
        return true;
    }

    public bool MarkDeceased(string viewerId, string gameDate, string cause)
    {
        if (!TryGet(viewerId, out var account) || account!.Current?.Status != ViewerLifeStatus.Active)
            return false;
        var life = account.Current;
        _claimed.Remove(life!.CitizenKey);
        life.CitizenKey = default!;
        life.Status = ViewerLifeStatus.Deceased;
        life.EndGameDate = gameDate;
        life.CauseOfDeath = cause;
        return true;
    }

    public bool MarkMissing(string viewerId)
    {
        if (!TryGet(viewerId, out var account) || account!.Current?.Status != ViewerLifeStatus.Active)
            return false;
        var life = account.Current;
        _claimed.Remove(life!.CitizenKey);
        life.CitizenKey = default!;
        life.Status = ViewerLifeStatus.Missing;
        return true;
    }

    public void UpdateIdentity(string viewerId, string login, string displayName)
    {
        if (!TryGet(viewerId, out var account)) return;
        account!.Login = login;
        account.DisplayName = displayName;
    }

    public void UpdateSnapshot(string viewerId, string age, string home, string workplace)
    {
        if (!TryGet(viewerId, out var account) || account!.Current?.Status != ViewerLifeStatus.Active)
            return;
        var life = account.Current!;
        life.LastKnownAge = age;
        life.LastKnownHome = home;
        life.LastKnownWorkplace = workplace;
    }

    public void Restore(IEnumerable<ViewerLifeAccount<TKey>> source, Func<TKey, bool> isValid)
    {
        var accounts = new Dictionary<string, ViewerLifeAccount<TKey>>(StringComparer.Ordinal);
        var claimed = new HashSet<TKey>();
        var lifeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in source)
        {
            if (string.IsNullOrWhiteSpace(account.TwitchUserId) || account.Lives.Count == 0 ||
                accounts.ContainsKey(account.TwitchUserId))
                throw new FormatException("Invalid or duplicate viewer in life history.");
            for (var i = 0; i < account.Lives.Count; i++)
            {
                var life = account.Lives[i];
                if (!Guid.TryParseExact(life.LifeId, "N", out _) || !lifeIds.Add(life.LifeId) ||
                    !Enum.IsDefined(typeof(ViewerLifeStatus), life.Status) ||
                    (i < account.Lives.Count - 1 && life.Status != ViewerLifeStatus.Deceased))
                    throw new FormatException("Invalid life history.");
                if (life.Status == ViewerLifeStatus.Active)
                {
                    if (!isValid(life.CitizenKey))
                    {
                        life.Status = ViewerLifeStatus.Missing;
                        life.CitizenKey = default!;
                    }
                    else if (!claimed.Add(life.CitizenKey))
                        throw new FormatException("Citizen is bound to multiple viewers.");
                }
                else life.CitizenKey = default!;
            }
            accounts.Add(account.TwitchUserId, account);
        }
        _accounts.Clear();
        foreach (var pair in accounts) _accounts.Add(pair.Key, pair.Value);
        _claimed.Clear();
        foreach (var key in claimed) _claimed.Add(key);
    }
}
