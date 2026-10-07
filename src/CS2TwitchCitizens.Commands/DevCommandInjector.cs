namespace CS2TwitchCitizens.Commands;

/// <summary>Explicit test hook. Game integration can call this after constructing its queue.</summary>
public static class DevCommandInjector
{
    public static void EnqueueSampleJoin(TwitchCommandQueue queue, DateTimeOffset receivedAt)
    {
        Enqueue(queue, "!join", receivedAt);
    }

    public static void EnqueueSampleSequence(TwitchCommandQueue queue, DateTimeOffset receivedAt)
    {
        EnqueueSampleJoin(queue, receivedAt);
        Enqueue(queue, "!me", receivedAt);
        EnqueueSampleJoin(queue, receivedAt);
    }

    private static void Enqueue(TwitchCommandQueue queue, string message, DateTimeOffset receivedAt)
    {
        if (queue is null) throw new ArgumentNullException(nameof(queue));
        if (!TwitchCommandParser.TryParse(
                message, "dev-user-1", "lxstjxck", "lxstjxck", receivedAt,
                out var command) || command is null)
            throw new InvalidOperationException("The built-in DEV command could not be parsed.");

        queue.Enqueue(command);
    }
}
