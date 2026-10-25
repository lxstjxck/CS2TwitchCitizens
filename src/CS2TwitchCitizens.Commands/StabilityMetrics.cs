using System.Diagnostics;

namespace CS2TwitchCitizens.Commands;

/// <summary>Low-volume runtime counters enabled only for a diagnostic stream.</summary>
public static class StabilityMetrics
{
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("CS2TWITCHCITIZENS_DIAGNOSTICS") == "1";
    private static long _bindingCalls, _bindingTicks;
    private static long _searchCalls, _searchTicks;
    private static long _uiCalls, _uiTicks;
    private static long _ledgerWrites, _ledgerTicks;
    private static long _jsonWrites, _jsonBytes;

    public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;
    public static void Binding(long start) => Record(start, ref _bindingCalls, ref _bindingTicks);
    public static void Search(long start) => Record(start, ref _searchCalls, ref _searchTicks);
    public static void Ui(long start) => Record(start, ref _uiCalls, ref _uiTicks);
    public static void Ledger(long start) => Record(start, ref _ledgerWrites, ref _ledgerTicks);
    public static void Json(int bytes)
    {
        if (!Enabled) return;
        Interlocked.Increment(ref _jsonWrites);
        Interlocked.Add(ref _jsonBytes, bytes);
    }
    private static void Record(long start, ref long count, ref long ticks)
    {
        if (start == 0) return;
        Interlocked.Increment(ref count);
        Interlocked.Add(ref ticks, Stopwatch.GetTimestamp() - start);
    }
    public static string ReadAndReset(int commands, int commandPeak, long rejected, long expired, int redemptions,
        int backgroundTasks)
    {
        if (!Enabled) return string.Empty;
        var bCount = Interlocked.Exchange(ref _bindingCalls, 0);
        var bTicks = Interlocked.Exchange(ref _bindingTicks, 0);
        var sCount = Interlocked.Exchange(ref _searchCalls, 0);
        var sTicks = Interlocked.Exchange(ref _searchTicks, 0);
        var uCount = Interlocked.Exchange(ref _uiCalls, 0);
        var uTicks = Interlocked.Exchange(ref _uiTicks, 0);
        var lCount = Interlocked.Exchange(ref _ledgerWrites, 0);
        var lTicks = Interlocked.Exchange(ref _ledgerTicks, 0);
        var jsonCount = Interlocked.Exchange(ref _jsonWrites, 0);
        var jsonBytes = Interlocked.Exchange(ref _jsonBytes, 0);
        return $"[Stability] binding={bCount}/{Milliseconds(bTicks):0.0}ms search={sCount}/{Milliseconds(sTicks):0.0}ms " +
            $"ui={uCount}/{Milliseconds(uTicks):0.0}ms ledger={lCount}/{Milliseconds(lTicks):0.0}ms " +
            $"json={jsonCount}/{jsonBytes}B commands={commands} commandPeak={commandPeak} " +
            $"rejected={rejected} expired={expired} " +
            $"redemptions={redemptions} backgroundTasks={backgroundTasks}";
    }
    private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}
