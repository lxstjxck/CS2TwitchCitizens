namespace CS2TwitchCitizens.Commands;

/// <summary>Explicit test hook. Game integration can call this after constructing its queue.</summary>
public static class DevCommandInjector
{
    public static void EnqueueSampleJoin(TwitchCommandQueue queue, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(queue);
        if (!TwitchCommandParser.TryParse(
                "!join", "dev-user-1", "devviewer", "DevViewer", receivedAt,
                out var command) || command is null)
            throw new InvalidOperationException("The built-in DEV command could not be parsed.");

        queue.Enqueue(command);
    }
}
