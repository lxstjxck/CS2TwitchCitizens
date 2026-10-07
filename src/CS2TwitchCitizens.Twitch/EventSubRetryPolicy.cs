namespace CS2TwitchCitizens.Twitch;

public static class EventSubRetryPolicy
{
    public static TimeSpan Delay(int failedAttempts)
    {
        var exponent = Math.Max(0, Math.Min(failedAttempts - 1, 5));
        return TimeSpan.FromSeconds(Math.Min(120, 2 << exponent));
    }
}
