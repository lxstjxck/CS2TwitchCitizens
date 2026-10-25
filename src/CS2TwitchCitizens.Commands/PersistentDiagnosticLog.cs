using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CS2TwitchCitizens.Commands;

/// <summary>
/// Always-on, bounded, best-effort diagnostic file writer. Never blocks the simulation
/// on disk I/O and never changes city save state. Explicitly omit sensitive fields.
/// </summary>
public sealed class PersistentDiagnosticLog : IDisposable
{
    public const long DefaultMaxFileBytes = 5 * 1024 * 1024;
    public const int DefaultArchiveCount = 3;
    private const int MaximumInputLength = 4096;
    private const int MaximumEntryLength = 1400;
    // Only values with known numeric meaning may be copied from game log messages.
    // In particular, never write arbitrary exception text or an unknown key=value pair.
    private static readonly Regex SafeCounters = new Regex(
        @"(?:^|\s)(viewers|queueDepth|rejected|expired|redemptions|tasks|logDropped|count|depth|lives|completed|commandPeak|backgroundTasks)=([0-9]{1,15})(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SafeLabels = new Regex(
        @"(?:^|\s)(stage|result|source)=([A-Za-z]+)(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> AllowedLabels = new HashSet<string>(StringComparer.Ordinal)
    {
        "Acknowledge", "Execute", "Policy", "ShowNext", "Test", "Snapshot", "Enqueue",
        "Preview", "EventSub", "received", "accepted", "queued", "active", "published",
        "QueueFull", "InvalidEvent", "CityNotReady", "LedgerUnavailable", "NoActiveEvent",
        "UIAcknowledgementMissing", "exception", "complete", "http", "start",
        "CameraUnavailable", "FocusFailed", "FocusRequested", "FollowRequested", "FollowStopped"
    };

    private readonly string _path;
    private readonly long _maxFileBytes;
    private readonly int _archiveCount;
    private readonly BlockingCollection<string> _pending;
    private readonly Thread _writer;
    private long _dropped;
    private int _disposed;
    private int _failed;

    public PersistentDiagnosticLog(string directory, long maxFileBytes = DefaultMaxFileBytes,
        int archiveCount = DefaultArchiveCount, int queueCapacity = 2048)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Log directory required.", nameof(directory));
        if (maxFileBytes < 128) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        if (archiveCount < 0 || archiveCount > 10) throw new ArgumentOutOfRangeException(nameof(archiveCount));
        if (queueCapacity < 1) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        _path = Path.Combine(directory, "CS2TwitchCitizens.log");
        _maxFileBytes = maxFileBytes;
        _archiveCount = archiveCount;
        _pending = new BlockingCollection<string>(new ConcurrentQueue<string>(), queueCapacity);
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "CS2TwitchCitizens diagnostic log" };
        _writer.Start();
    }

    public string FilePath => _path;
    public long Dropped => Interlocked.Read(ref _dropped);
    public bool Failed => Volatile.Read(ref _failed) != 0;

    public bool TryWrite(string message)
    {
        if (Volatile.Read(ref _disposed) != 0 || Failed || string.IsNullOrEmpty(message)) return false;
        var safe = Sanitize(message.Length > MaximumInputLength ? message.Substring(0, MaximumInputLength) : message);
        var maxMessageLength = (int)Math.Min(MaximumEntryLength, _maxFileBytes - 64);
        if (safe.Length > maxMessageLength) safe = safe.Substring(0, maxMessageLength);
        var line = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " [INFO] " + safe;
        try
        {
            if (_pending.TryAdd(line)) return true;
        }
        catch (InvalidOperationException) { } // Concurrent Dispose completed or closed the collection.
        Interlocked.Increment(ref _dropped);
        return false;
    }

    public static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        // Derive the event from fixed prefixes. No caller-supplied words are copied.
        string category;
        if (message.StartsWith("[CS2TwitchCitizens] START", StringComparison.Ordinal)) category = "START";
        else if (message.StartsWith("[CS2TwitchCitizens] STOP", StringComparison.Ordinal)) category = "STOP";
        else if (message.StartsWith("[CS2TwitchCitizens] HEALTH", StringComparison.Ordinal)) category = "HEALTH";
        else if (message.StartsWith("[CS2TwitchCitizens] Twitch", StringComparison.Ordinal))
        {
            if (message.Contains("EventSub session established")) category = "EVENTSUB_SESSION";
            else if (message.Contains("EventSub session reconnected")) category = "EVENTSUB_RECONNECT";
            else if (message.Contains("subscription revoked")) category = "EVENTSUB_REVOKED";
            else if (message.Contains("subscription")) category = "TWITCH_SUBSCRIPTION";
            else if (message.Contains("reconnect")) category = "TWITCH_RECONNECT";
            else if (message.Contains("disconnected")) category = "TWITCH_DISCONNECT";
            else if (message.Contains("connected")) category = "TWITCH_CONNECT";
            else if (message.Contains("error") || message.Contains("failed")) category = "TWITCH_ERROR";
            else category = "TWITCH";
        }
        else if (message.StartsWith("[CS2TwitchCitizens] LIFE", StringComparison.Ordinal))
            category = message.Contains("restore failed") ? "SAVE_RESTORE_FAILED" :
                message.Contains("restore complete") ? "SAVE_RESTORE_COMPLETE" : "SAVE";
        else if (message.StartsWith("[CS2TwitchCitizens] TWITCH COMMAND", StringComparison.Ordinal)) category = "COMMAND_RECEIVED";
        else if (message.StartsWith("[CS2TwitchCitizens] JOIN", StringComparison.Ordinal)) category = "COMMAND_JOIN";
        else if (message.StartsWith("[CS2TwitchCitizens] !me", StringComparison.Ordinal)) category = "COMMAND_ME";
        else if (message.StartsWith("[CS2TwitchCitizens] HISTORY", StringComparison.Ordinal)) category = "COMMAND_HISTORY";
        else if (message.StartsWith("[CS2TwitchCitizens] NAME", StringComparison.Ordinal)) category = "COMMAND_NAME";
        else if (message.StartsWith("[CS2TwitchCitizens] FIND", StringComparison.Ordinal)) category = "COMMAND_FIND";
        else if (message.StartsWith("[CS2TwitchCitizens] CAMERA", StringComparison.Ordinal)) category = "CAMERA";
        else if (message.StartsWith("[CS2TwitchCitizens] UI", StringComparison.Ordinal)) category = "UI";
        else if (message.StartsWith("[Rewards]", StringComparison.Ordinal)) category = "REWARDS";
        else if (message.StartsWith("[Interactive]", StringComparison.Ordinal)) category = "INTERACTIVE";
        else if (message.StartsWith("[Stability]", StringComparison.Ordinal)) category = "STABILITY";
        else category = "SYSTEM";

        var safe = new StringBuilder("event=").Append(category);
        if (category == "HEALTH" || category == "SAVE" || category == "SAVE_RESTORE_COMPLETE" ||
            category == "REWARDS" || category == "STABILITY" || category == "TWITCH_SUBSCRIPTION" ||
            category == "TWITCH")
            foreach (Match match in SafeCounters.Matches(message))
                safe.Append(' ').Append(match.Groups[1].Value).Append('=').Append(match.Groups[2].Value);
        if (category == "INTERACTIVE" || category == "REWARDS" || category == "CAMERA" || category == "UI")
            foreach (Match match in SafeLabels.Matches(message))
                if (AllowedLabels.Contains(match.Groups[2].Value))
                    safe.Append(' ').Append(match.Groups[1].Value).Append('=').Append(match.Groups[2].Value);
        if (category == "HEALTH")
        {
            safe.Append(" cityLoaded=").Append(message.Contains("cityLoaded=True") ? "true" : "false");
            safe.Append(" eventSubConnected=").Append(message.Contains("eventSub=Connected") ? "true" : "false");
            safe.Append(" pointsConnected=").Append(message.Contains("points=Connected") ? "true" : "false");
        }
        return safe.ToString();
    }

    private void WriteLoop()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            if (File.Exists(_path) && new FileInfo(_path).Length >= _maxFileBytes) RotateFiles();
            var writer = OpenWriter();
            try
            {
                var size = new FileInfo(_path).Length;
                while (!_pending.IsCompleted)
                {
                    if (_pending.TryTake(out var line, 250))
                    {
                        var entryBytes = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine);
                        if (size > 0 && size + entryBytes > _maxFileBytes)
                        {
                            writer.Flush();
                            writer.Dispose();
                            RotateFiles();
                            writer = OpenWriter();
                            size = 0;
                        }
                        writer.WriteLine(line);
                        size += entryBytes;
                    }
                    // Flush even when idle, so the last event survives unexpected game termination.
                    writer.Flush();
                }
            }
            finally { writer.Dispose(); }
        }
        catch (Exception)
        {
            // File permissions, full disks, and shutdown races must never crash the game.
            Volatile.Write(ref _failed, 1);
        }
    }

    private StreamWriter OpenWriter() =>
        new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite, 4096), new UTF8Encoding(false));

    private string ArchivePath(int index) =>
        Path.Combine(Path.GetDirectoryName(_path)!, "CS2TwitchCitizens." + index + ".log");

    private void RotateFiles()
    {
        if (_archiveCount == 0)
        {
            if (File.Exists(_path)) File.Delete(_path);
            return;
        }
        var oldest = ArchivePath(_archiveCount);
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var i = _archiveCount - 1; i >= 1; i--)
        {
            var source = ArchivePath(i);
            if (File.Exists(source)) File.Move(source, ArchivePath(i + 1));
        }
        if (File.Exists(_path)) File.Move(_path, ArchivePath(1));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _pending.CompleteAdding();
        // Bounded shutdown: never stall Cities: Skylines II indefinitely on file I/O.
        if (_writer.Join(TimeSpan.FromSeconds(2))) _pending.Dispose();
    }
}
