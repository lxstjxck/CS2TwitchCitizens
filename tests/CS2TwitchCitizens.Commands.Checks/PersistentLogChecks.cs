using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CS2TwitchCitizens.Commands;

internal static class PersistentLogChecks
{
    public static void Run()
    {
        var scrubbed = PersistentDiagnosticLog.Sanitize(
            "[CS2TwitchCitizens] JOIN viewer=123 login=alice citizen=Entity(5:1) " +
            "token=secret123 text=private message body next=safe Authorization: Bearer abcdef\nHEALTH");
        Require(scrubbed == "event=COMMAND_JOIN", "command logs must contain only a fixed category");
        Require(PersistentDiagnosticLog.Sanitize("unknown error=private-user-id count=123") == "event=SYSTEM",
            "unknown log formats must never copy arbitrary fields");
        Require(PersistentDiagnosticLog.Sanitize("[Interactive] stage=ShowNext result=active viewer=123 text=private") ==
            "event=INTERACTIVE stage=ShowNext result=active", "interactive stages retain only allowlisted labels");
        Require(PersistentDiagnosticLog.Sanitize("[CS2TwitchCitizens] HEALTH cityLoaded=True viewers=5 " +
            "eventSub=Connected points=Disconnected queueDepth=3 logDropped=1 token=secret") ==
            "event=HEALTH viewers=5 queueDepth=3 logDropped=1 cityLoaded=true eventSubConnected=true pointsConnected=false",
            "heartbeat retains safe counters and connection flags");

        var directory = Path.Combine(Path.GetTempPath(), "cs2tc-persistent-log-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var logger = new PersistentDiagnosticLog(directory, maxFileBytes: 512,
                archiveCount: 2, queueCapacity: 128))
            {
                for (var i = 0; i < 30; i++)
                    logger.TryWrite("[Interactive] stage=Enqueue result=accepted viewer=private-viewer");
                logger.TryWrite("[CS2TwitchCitizens] HEALTH viewers=7 eventSub=Connected");
            }

            var files = Directory.GetFiles(directory, "*.log");
            Require(files.Length >= 1 && files.Length <= 3, "bounded log rotation");
            var active = Path.Combine(directory, "CS2TwitchCitizens.log");
            Require(File.Exists(active), "active diagnostic log must exist");
            var all = string.Join("\n", files.Select(File.ReadAllText));
            Require(all.Contains("event=HEALTH viewers=7") && all.Contains("eventSubConnected=true"),
                "last status log must persist on shutdown");
            Require(!all.Contains("private-viewer"), "viewer ID must not reach disk");
            Require(files.All(file => new FileInfo(file).Length <= 512), "all logs stay within byte limit");
            Require(all.Split('\n').Where(line => line.Length > 0).All(line =>
                DateTimeOffset.TryParse(line.Substring(0, Math.Min(33, line.Length)), out _)),
                "each log line begins with a UTC timestamp");
            using (var reopened = new PersistentDiagnosticLog(directory, maxFileBytes: 512, archiveCount: 2))
                reopened.TryWrite("[CS2TwitchCitizens] START version=0.9.7");
            Require(File.ReadAllText(active).Contains("event=START"), "logging survives restart");

            var smallDirectory = Path.Combine(directory, "small");
            using (var small = new PersistentDiagnosticLog(smallDirectory, maxFileBytes: 128))
                small.TryWrite("[CS2TwitchCitizens] HEALTH " + string.Concat(Enumerable.Repeat("viewers=12345 ", 400)));
            Require(new FileInfo(Path.Combine(smallDirectory, "CS2TwitchCitizens.log")).Length <= 128,
                "oversized input cannot exceed the file limit");

            var overflowDirectory = Path.Combine(directory, "overflow");
            using (var overflow = new PersistentDiagnosticLog(overflowDirectory, queueCapacity: 1))
            {
                var started = Stopwatch.StartNew();
                for (var i = 0; i < 10000; i++) overflow.TryWrite("[Interactive] stage=Enqueue result=accepted");
                Require(overflow.Dropped > 0, "full queue drops writes");
                Require(started.Elapsed < TimeSpan.FromSeconds(5), "full queue does not block the caller");
            }

            var racing = new PersistentDiagnosticLog(Path.Combine(directory, "race"), queueCapacity: 8);
            using var raceStarted = new ManualResetEventSlim();
            var producer = Task.Run(() =>
            {
                raceStarted.Set();
                for (var i = 0; i < 10000; i++) racing.TryWrite("[Interactive] stage=Enqueue result=accepted");
            });
            raceStarted.Wait();
            racing.Dispose();
            producer.GetAwaiter().GetResult();

            var blockedPath = Path.Combine(directory, "blocked");
            File.WriteAllText(blockedPath, "file, not a directory");
            using (var failed = new PersistentDiagnosticLog(blockedPath))
            {
                failed.TryWrite("[CS2TwitchCitizens] START");
                Require(SpinWait.SpinUntil(() => failed.Failed, TimeSpan.FromSeconds(5)),
                    "unavailable directory disables only file logging");
                Require(!failed.TryWrite("[CS2TwitchCitizens] STOP"), "failed writer rejects later events");
            }
            Console.WriteLine("PASS: private bounded diagnostic log, rotation, restart and I/O failure");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void Require(bool pass, string description)
    {
        if (!pass) throw new Exception("Persistent logging check failed: " + description);
    }
}
