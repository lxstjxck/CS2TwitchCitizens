namespace CS2TwitchCitizens.Twitch;

public static class EventSubRetryPolicy
{
    public static TimeSpan Delay(int failedAttempts)
    {
        var exponent = Math.Max(0, Math.Min(failedAttempts - 1, 5));
        return TimeSpan.FromSeconds(Math.Min(120, 2 << exponent));
    }
}

/// <summary>Tracks consecutive failed sessions, including a clean socket close.</summary>
public sealed class EventSubRetryTracker
{
    private int _failures;
    public int Failures => _failures;
    public void Connected() => _failures = 0;
    public TimeSpan Disconnected() => EventSubRetryPolicy.Delay(++_failures);
}
