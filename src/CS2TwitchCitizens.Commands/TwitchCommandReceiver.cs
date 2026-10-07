namespace CS2TwitchCitizens.Commands;

/// <summary>
/// A framework-neutral drain for a future game system to call on its update thread.
/// The callback is invoked on the caller's thread and must not perform network I/O.
/// </summary>
public static class TwitchCommandReceiver
{
    public static int Drain(TwitchCommandQueue queue, int maximum, Action<TwitchCommand> receive)
    {
        if (queue is null) throw new ArgumentNullException(nameof(queue));
        if (receive is null) throw new ArgumentNullException(nameof(receive));
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));

        var processed = 0;
        while (processed < maximum && queue.TryDequeue(out var command))
        {
            receive(command!);
            processed++;
        }

        return processed;
    }
}
