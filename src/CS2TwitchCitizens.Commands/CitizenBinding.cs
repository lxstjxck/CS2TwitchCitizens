namespace CS2TwitchCitizens.Commands;

/// <summary>
/// A future binding shape. TKey must be chosen only after a persistent CS2 citizen
/// identifier is verified. No persistence or Entity assumption is made here.
/// </summary>
public sealed class CitizenBinding<TKey> where TKey : notnull
{
    public CitizenBinding(string twitchUserId, TKey citizenKey)
    {
        if (string.IsNullOrWhiteSpace(twitchUserId))
            throw new ArgumentException("A Twitch user ID is required.", nameof(twitchUserId));

        TwitchUserId = twitchUserId;
        CitizenKey = citizenKey;
    }

    public string TwitchUserId { get; }
    public TKey CitizenKey { get; }
}
