using System.Diagnostics;
using System.Text.Json;
using CS2TwitchCitizens.Commands;
using CS2TwitchCitizens.Twitch;

internal static class StabilityChecks
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var queue = new TwitchCommandQueue(128, TimeSpan.FromMinutes(2), () => now);
        var producer = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
            queue.TryEnqueue(Command(i.ToString(), now));
        producer.Stop();
        Require(queue.Count == 128 && queue.Rejected == 872 && queue.Expired == 0,
            "1000 command burst must remain bounded and count overflow");
        var concurrent = new TwitchCommandQueue(128, clock: () => now);
        Parallel.For(0, 1000, i => concurrent.TryEnqueue(Command("parallel-" + i, now)));
        Require(concurrent.Count == 128 && concurrent.Rejected == 872,
            "parallel producer burst must remain bounded");
        now = now.AddMinutes(3);
        Require(!queue.TryDequeue(out _) && queue.Expired == 128 && queue.Count == 0,
            "commands waiting through an outage must expire");
        Require(!queue.TryEnqueue(Command("stale", now.AddMinutes(-3))) && queue.Expired == 129,
            "late EventSub commands must expire on admission");
        Require(queue.TryEnqueue(Command("fresh", now)) && queue.TryDequeue(out var fresh) &&
            fresh!.TwitchUserId == "fresh", "fresh command must remain usable after expiry");

        var retry = new EventSubRetryTracker();
        Require(retry.Disconnected() == TimeSpan.FromSeconds(2), "initial reconnect delay");
        for (var i = 0; i < 30; i++) retry.Disconnected(); // local simulation of a 30-second outage
        retry.Connected();
        Require(retry.Failures == 0 && retry.Disconnected() == TimeSpan.FromSeconds(2),
            "successful recovery must reset backoff after a 30-second outage");
        for (var i = 0; i < 300; i++) retry.Disconnected(); // local simulation of a five-minute outage
        retry.Connected();
        Require(retry.Disconnected() == TimeSpan.FromSeconds(2),
            "successful recovery must reset backoff after a five-minute outage");
        var ids = new EventSubMessageIds(1024);
        Require(ids.TryAdd("notification-1") && !ids.TryAdd("notification-1"),
            "duplicate EventSub notification must be suppressed");

        var restartQueue = new TwitchCommandQueue(clock: () => now);
        restartQueue.Enqueue(Command("before-restart", now));
        using (var controller = new TwitchConnectionController("",
            new TwitchCredentialStore(Path.Combine(Path.GetTempPath(), "unused-cs2tc-" + Guid.NewGuid().ToString("N"))),
            new TwitchOAuthClient(new HttpClient()), restartQueue, _ => { }, false))
        {
            var generation = controller.SessionGeneration;
            controller.Reconnect(); // Empty client ID means this test performs no OAuth request.
            Require(controller.SessionGeneration == generation + 1 && restartQueue.Count == 0,
                "manual restart must invalidate old work and clear old commands");
        }

        var root = Path.Combine(Path.GetTempPath(), "cs2tc-stability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ledgerPath = Path.Combine(root, "redemptions.json");
            var ledger = new RedemptionLedger(ledgerPath);
            var ledgerWrites = Stopwatch.StartNew();
            for (var i = 0; i < 100; i++)
                Require(ledger.TryReserve("42", "redemption-" + i), "ledger reservation");
            ledgerWrites.Stop();
            Require(!ledger.TryReserve("42", "redemption-0"), "redemption cannot be reserved twice");
            var journal = Path.Combine(root, "redemptions", "42.json");
            var original = File.ReadAllText(journal);
            File.Move(journal, journal + ".saved");
            Directory.CreateDirectory(journal); // deterministic write failure, without ACL changes
            var failed = false;
            try { ledger.Finish("42", "redemption-0", RedemptionLedger.ManualReview); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            Require(failed && ledger.Get("42", "redemption-0") == RedemptionLedger.Received,
                "failed JSON write must leave in-memory state unchanged");
            Directory.Delete(journal);
            File.Move(journal + ".saved", journal);
            Require(File.ReadAllText(journal) == original, "failed JSON write must preserve old journal");
            ledger.Finish("42", "redemption-0", RedemptionLedger.ManualReview);
            Require(new RedemptionLedger(ledgerPath).Get("42", "redemption-0") == RedemptionLedger.ManualReview,
                "ledger state must survive reload");
            Require(ledger.TryMarkManualReviewIfUnknown("42", "missed-after-outage") &&
                !ledger.TryMarkManualReviewIfUnknown("42", "missed-after-outage") &&
                !ledger.TryReserve("42", "missed-after-outage") &&
                new RedemptionLedger(ledgerPath).Get("42", "missed-after-outage") == RedemptionLedger.ManualReview,
                "missed redemption must be persisted for manual review and never replayed");
            Require(!ledger.TryMarkManualReviewIfUnknown("42", "redemption-1") &&
                ledger.Get("42", "redemption-1") == RedemptionLedger.Received,
                "reconciliation must not overwrite a redemption already claimed by EventSub");
            Require(ledger.MarkManualReviewUnknown("42", new[] {
                "redemption-1", "missed-batch", "missed-batch" }) == 1 &&
                ledger.Get("42", "redemption-1") == RedemptionLedger.Received &&
                new RedemptionLedger(ledgerPath).Get("42", "missed-batch") == RedemptionLedger.ManualReview,
                "batch reconciliation must deduplicate and preserve claimed states");

            var damaged = Path.Combine(root, "damaged");
            Directory.CreateDirectory(damaged);
            var damagedPath = Path.Combine(damaged, "redemptions.json");
            File.WriteAllText(damagedPath, "{\"partial\":");
            var corrupt = new RedemptionLedger(damagedPath);
            Require(corrupt.LoadFailed && !corrupt.TryReserve("42", "new") &&
                File.ReadAllText(damagedPath) == "{\"partial\":",
                "partial legacy ledger must block writes and remain recoverable");
            var damagedJournal = Path.Combine(damaged, "redemptions", "42.json");
            Directory.CreateDirectory(Path.GetDirectoryName(damagedJournal)!);
            File.WriteAllText(damagedJournal, "{\"partial\":");
            File.Delete(damagedPath);
            var corruptJournal = new RedemptionLedger(damagedPath);
            Require(corruptJournal.LoadFailed && !corruptJournal.TryReserve("42", "new") &&
                File.ReadAllText(damagedJournal) == "{\"partial\":",
                "partial per-channel ledger must block writes and remain recoverable");
            var rulesPath = Path.Combine(root, "interactive.json");
            var rules = new RewardActionRegistry(rulesPath);
            var ruleSettings = InteractiveSettings.Defaults();
            ruleSettings.Rewards.Add(new RewardRule { RewardId = "reward-42" });
            rules.Update(ruleSettings.ToJson());
            Require(new RewardActionRegistry(rulesPath).Find("reward-42")?.RewardId == "reward-42",
                "Twitch reward ID must survive a restart");
            Console.WriteLine($"PASS: ledger writes=100 elapsedMs={ledgerWrites.ElapsedMilliseconds} bytes={original.Length}, corruption and write rollback");
        }
        finally { Directory.Delete(root, true); }

        var population = Enumerable.Range(0, 300_000).Select(_ => new CitizenEligibilityFacts(
            true, false, false, false, false, true, true, true, false, true, true)).ToArray();
        var scan = Stopwatch.StartNew();
        var eligible = population.Count(candidate => CitizenEligibilityPolicy.Check(candidate) == CitizenRejection.None);
        scan.Stop();
        Require(eligible == 300_000, "300k local population model");
        var lives = new ViewerLifeJournal<int>();
        for (var i = 0; i < 500; i++)
        {
            var id = "viewer-" + i;
            Require(lives.TryStart(id, id, id, i + 1, "2026-01-01", "", "Adult", "Home", "Work", out _),
                "bulk viewer binding");
            if (i % 2 == 0)
            {
                Require(lives.MarkDeceased(id, "2026-02-01", "Natural") &&
                    lives.TryStart(id, id, id, i + 1001, "2026-03-01", "", "Adult", "Home", "Work", out _),
                    "life history in bulk viewer set");
            }
        }
        var serialization = Stopwatch.StartNew();
        var json = JsonSerializer.Serialize(lives.Accounts);
        var restored = new ViewerLifeJournal<int>();
        restored.Restore(JsonSerializer.Deserialize<List<ViewerLifeAccount<int>>>(json)!, _ => true);
        serialization.Stop();
        Require(restored.Accounts.Count == 500 && restored.TryGet("viewer-0", out var first) &&
            first!.Lives.Count == 2 && first.Lives[0].Status == ViewerLifeStatus.Deceased &&
            first.Current!.Status == ViewerLifeStatus.Active,
            "hundreds of viewers and previous lives must survive roundtrip");
        var panelViewers = Enumerable.Range(0, 500).Select(i => new ViewerCitizenInfo {
            TwitchUserId = "viewer-" + i, CitizenName = "Citizen " + i, IsValid = true
        }).ToArray();
        var panelWatch = Stopwatch.StartNew();
        var panelJson = ViewerPanelSnapshot.Create("Connected", "42", true, panelViewers).ToJson();
        panelWatch.Stop();
        using (var panel = JsonDocument.Parse(panelJson))
            Require(panel.RootElement.GetProperty("viewers").GetArrayLength() == 500,
                "500 viewer panel snapshot must serialize completely");
        Console.WriteLine($"PASS: local burstMs={producer.ElapsedMilliseconds} queuePeak=128 rejected={queue.Rejected} " +
            $"population=300000 policyScanMs={scan.ElapsedMilliseconds} viewers=500 " +
            $"historyJsonBytes={json.Length} historyRoundtripMs={serialization.ElapsedMilliseconds} " +
            $"panelJsonBytes={panelJson.Length} panelSerializeMs={panelWatch.ElapsedMilliseconds}; " +
            "no game ECS or Twitch network used");
    }

    private static TwitchCommand Command(string id, DateTimeOffset receivedAt) =>
        new(id, id, id, "!join", "", receivedAt);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Stability check failed: " + message);
    }
}
