using System;
using System.IO;
using System.Linq;
using CS2TwitchCitizens.Commands;

internal static class PersistentLogChecks
{
    public static void Run()
    {
        var scrubbed = PersistentDiagnosticLog.Sanitize(
            "[CS2TwitchCitizens] JOIN viewer=123 login=alice citizen=Entity(5:1) " +
            "token=secret123 text=private message body next=safe");
        Require(!scrubbed.Contains("123") && !scrubbed.Contains("alice") &&
            !scrubbed.Contains("Entity(") && !scrubbed.Contains("secret123") &&
            !scrubbed.Contains("private message body") && scrubbed.Contains("next=safe"),
            "sensitive fields must not reach the diagnostic file");
        Require(!PersistentDiagnosticLog.Sanitize("Authorization: Bearer abcdef")
            .Contains("abcdef"), "bearer tokens must be removed");
        Require(!PersistentDiagnosticLog.Sanitize("line 1\nline 2").Contains("\n"),
            "log lines must be single line");

        var directory = Path.Combine(Path.GetTempPath(),
            "cs2tc-persistent-log-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var logger = new PersistentDiagnosticLog(directory, maxFileBytes: 512,
                archiveCount: 2, queueCapacity: 128))
            {
                for (var i = 0; i < 30; i++)
                    logger.TryWrite("[CS2TwitchCitizens] TEST item=" + i +
                        " payload=ABCDEFGHIJKLMNOPQRSTUVWXYZ");
                logger.TryWrite("HEALTH viewer=private-viewer state=Connected");
            }

            var files = Directory.GetFiles(directory, "*.log");
            Require(files.Length >= 1 && files.Length <= 3, "bounded log rotation");
            var active = Path.Combine(directory, "CS2TwitchCitizens.log");
            Require(File.Exists(active), "active diagnostic log must exist");
            var all = string.Join("\n", files.Select(File.ReadAllText));
            Require(all.Contains("HEALTH") && all.Contains("state=Connected"),
                "critical status log must persist on shutdown");
            Require(!all.Contains("private-viewer"), "viewer ID must be redacted on disk");
            using (var reopened = new PersistentDiagnosticLog(directory, maxFileBytes: 512, archiveCount: 2))
                reopened.TryWrite("RESTART test");
            Require(File.ReadAllText(active).Contains("RESTART test"),
                "logging must persist across game sessions");
            Console.WriteLine("PASS: sanitized always-on diagnostic log, restart and bounded rotation");
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
