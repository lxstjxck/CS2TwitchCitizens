using System.Text.Json;
using System.Net;
using System.Net.Http;
using CS2TwitchCitizens.Commands;
using CS2TwitchCitizens.Twitch;

internal static class InteractiveChecks
{
    public static void Run()
    {
        var payload = """
            {"metadata":{"message_id":"message-1","message_type":"notification"},
             "payload":{"subscription":{"type":"channel.channel_points_custom_reward_redemption.add"},
               "event":{"id":"redemption-1","broadcaster_user_id":"42","user_id":"9001",
                 "user_name":"Зритель","user_input":"Привет, город!","status":"unfulfilled",
                 "reward":{"id":"reward-1"}}}}
            """;
        Check(EventSubProtocol.TryRead(payload, out var envelope));
        Check(EventSubProtocol.TryGetRedemption(envelope!, "42", DateTimeOffset.UtcNow, out var redemption));
        Check(redemption!.RedemptionId == "redemption-1" && redemption.TwitchUserId == "9001" &&
            redemption.UserInput == "Привет, город!" && redemption.RewardId == "reward-1");
        Check(!EventSubProtocol.TryGetRedemption(envelope!, "wrong", DateTimeOffset.UtcNow, out _));
        using (var chatBody = JsonDocument.Parse(TwitchEventSubService.BuildSubscriptionJson(
            "channel.chat.message", "42", "42", "session")))
            Check(chatBody.RootElement.GetProperty("condition").GetProperty("user_id").GetString() == "42");
        using (var rewardBody = JsonDocument.Parse(TwitchEventSubService.BuildSubscriptionJson(
            EventSubProtocol.RedemptionType, "42", null, "session")))
            Check(rewardBody.RootElement.GetProperty("condition").GetProperty("broadcaster_user_id").GetString() == "42" &&
                !rewardBody.RootElement.GetProperty("condition").TryGetProperty("user_id", out _));
        foreach (var invalid in new[] {
            payload.Replace("\"reward-1\"", "null"),
            payload.Replace("\"unfulfilled\"", "\"canceled\""),
            payload.Replace("\"redemption-1\"", "\"\""),
            payload.Replace("\"channel.channel_points_custom_reward_redemption.add\"", "\"channel.chat.message\"")
        })
        {
            Check(EventSubProtocol.TryRead(invalid, out var bad));
            Check(!EventSubProtocol.TryGetRedemption(bad!, "42", DateTimeOffset.UtcNow, out _));
        }
        var canceledPayload = payload.Replace("redemption.add", "redemption.update")
            .Replace("\"unfulfilled\"", "\"canceled\"");
        Check(EventSubProtocol.TryRead(canceledPayload, out var canceledEnvelope) &&
            EventSubProtocol.TryGetRedemption(canceledEnvelope!, "42", DateTimeOffset.UtcNow, out var cancellation) &&
            cancellation!.Status == "canceled" && cancellation.IsUpdate);
        var ids = new EventSubMessageIds(2);
        Check(ids.TryAdd("m1") && !ids.TryAdd("m1") && ids.TryAdd("m2") && ids.TryAdd("m3") && ids.TryAdd("m1"));
        var queue = new RedemptionQueue(2);
        Check(queue.TryEnqueue(redemption) && queue.TryEnqueue(redemption) && !queue.TryEnqueue(redemption));
        Check(queue.Count == 2 && queue.TryDequeue(out _) && queue.Count == 1);
        queue.Clear();
        Check(queue.Count == 0);
        var displayClock = DateTimeOffset.UtcNow;
        var presentation = new InteractivePresentationQueue();
        var previewMessage = new CityEventRecord { Id = "preview-message", Type = InteractiveAction.ShowMessage,
            LocalTest = true, Text = "Preview" };
        var previewAnnouncement = new CityEventRecord { Id = "preview-announcement",
            Type = InteractiveAction.CityAnnouncement, LocalTest = true, Text = "Preview announcement" };
        Check(presentation.TryEnqueue(previewMessage) && presentation.TryEnqueue(previewAnnouncement));
        Check(presentation.NeedsAdvance(displayClock) && presentation.Advance(displayClock) == null &&
            presentation.Active?.Id == "preview-message" &&
            presentation.Active.State == "AwaitingDisplay" && presentation.PendingCount == 1);
        Check(!presentation.Acknowledge("wrong", displayClock) &&
            !presentation.NeedsAdvance(displayClock.AddSeconds(8)));
        Check(presentation.Acknowledge("preview-message", displayClock.AddSeconds(2)) &&
            !presentation.Acknowledge("preview-message", displayClock.AddSeconds(2)) &&
            !presentation.NeedsAdvance(displayClock.AddSeconds(9)) &&
            presentation.NeedsAdvance(displayClock.AddSeconds(10)));
        Check(presentation.Advance(displayClock.AddSeconds(10)) == previewMessage &&
            presentation.Active?.Id == "preview-announcement" && previewMessage.State == "Completed");
        Check(presentation.Acknowledge("preview-announcement", displayClock.AddSeconds(11)) &&
            !presentation.NeedsAdvance(displayClock.AddSeconds(40)) &&
            presentation.NeedsAdvance(displayClock.AddSeconds(41)));
        Check(presentation.Advance(displayClock.AddSeconds(41)) == previewAnnouncement &&
            presentation.Active == null);
        var hiddenRedemption = new CityEventRecord { Id = "redemption", Type = InteractiveAction.ShowMessage,
            RedemptionId = "redeem-1" };
        Check(presentation.TryEnqueue(hiddenRedemption));
        presentation.Advance(displayClock);
        Check(hiddenRedemption.State == "AwaitingDisplay" &&
            presentation.NeedsAdvance(displayClock.AddSeconds(30)) &&
            !presentation.Acknowledge("wrong", displayClock.AddSeconds(1)));
        var removed = presentation.Reset();
        Check(removed.Length == 1 && removed[0] == hiddenRedemption && presentation.Active == null);
        var canceled = new CityEventRecord { Id = "canceled", RedemptionId = "redeem-2",
            Type = InteractiveAction.CityAnnouncement };
        Check(presentation.TryEnqueue(canceled));
        presentation.RemoveRedemption("redeem-2");
        Check(presentation.PendingCount == 0 && presentation.Active == null);
        using (var snapshot = JsonDocument.Parse(new CityEventSnapshot { Queued = 2, Dropped = 1,
            Active = new CityEventRecord { Id = "local-1", Type = InteractiveAction.CityAnnouncement,
                Text = "Город празднует", LocalTest = true } }.ToJson()))
            Check(snapshot.RootElement.GetProperty("queued").GetInt32() == 2 &&
                snapshot.RootElement.GetProperty("dropped").GetInt32() == 1 &&
                snapshot.RootElement.GetProperty("active").GetProperty("localTest").GetBoolean() &&
                snapshot.RootElement.GetProperty("active").GetProperty("text").GetString() == "Город празднует");
        Check(TwitchRewardCatalog.SafeIcon("https://static-cdn.jtvnw.net/custom-reward-images/x.png") != null);
        Check(TwitchRewardCatalog.SafeIcon("https://evil.example/icon.png") == null);
        Check(TwitchRewardCatalog.SafeIcon("http://static-cdn.jtvnw.net/x.png") == null);
        var icons = TwitchRewardCatalog.LoadAsync(new HttpClient(new RewardHandler()), "client", "secret", "42",
            CancellationToken.None).GetAwaiter().GetResult();
        Check(icons["reward-1"].StartsWith("https://static-cdn.jtvnw.net/") && !icons.ContainsKey("reward-2"));

        var directory = Path.Combine(Path.GetTempPath(), "cs2-interactive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsPath = Path.Combine(directory, "interactive.json");
            var registry = new RewardActionRegistry(settingsPath);
            Check(!registry.Current.Enabled && registry.Find("reward-1") == null);
            registry.Update("{\"version\":1,\"enabled\":true,\"rewards\":[{\"rewardId\":\"reward-1\",\"action\":\"ShowMessage\",\"enabled\":true,\"maxTextLength\":180,\"cooldownSeconds\":15,\"displaySeconds\":8}]}");
            Check(registry.Current.Enabled && registry.Find("reward-1")?.Action == InteractiveAction.ShowMessage);
            var rule = registry.Find("reward-1")!;
            Check(!rule.AutoFulfill);
            var autoSettings = registry.Current;
            autoSettings.Rewards[0].AutoFulfill = true;
            registry.Update(autoSettings.ToJson());
            Check(new RewardActionRegistry(settingsPath).Find("reward-1")?.AutoFulfill == true);
            rule = registry.Find("reward-1")!;
            var now = DateTimeOffset.UtcNow;
            Check(InteractivePolicy.Check(registry.Current, rule, redemption, null, now, out var text) == "Ready" &&
                text == "Привет, город!");
            Check(InteractivePolicy.Check(registry.Current,
                new RewardRule { RewardId = "reward-2", Action = InteractiveAction.CityAnnouncement },
                redemption, null, now, out _) == "Ready");
            Check(InteractivePolicy.Check(registry.Current, null, redemption, null, now, out _) == "UnknownOrDisabledReward");
            Check(InteractivePolicy.Check(registry.Current, rule, redemption, now, now, out _) == "Cooldown");
            redemption.UserInput = " \t ";
            Check(InteractivePolicy.Check(registry.Current, rule, redemption, null, now, out _) == "EmptyMessage");
            Check(InteractivePolicy.Check(registry.Current,
                new RewardRule { RewardId = "reward-1", Action = InteractiveAction.CityCelebration },
                redemption, null, now, out _) == "LegacyRewardUnavailable");
            redemption.UserInput = new string('x', 500);
            Check(InteractivePolicy.Check(registry.Current, rule, redemption, null, now, out text) == "Ready" &&
                text.Length == 180);
            redemption.UserInput = "<script>alert(1)</script>\u202E";
            Check(InteractivePolicy.Check(registry.Current, rule, redemption, null, now, out text) == "Ready" &&
                !text.Contains('\u202E'));
            Check(InteractivePolicy.Clean("A\r\nB", 20) == "A\nB");
            redemption.UserInput = "Привет, город!";
            Check(new RewardActionRegistry(settingsPath).Find("reward-1") != null);
            var rejected = false;
            try { registry.Update("{\"version\":1,\"rewards\":[{\"rewardId\":\"x\",\"action\":\"NotKnown\"}]}"); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && registry.Find("reward-1") != null);
            var ledgerPath = Path.Combine(directory, "redemptions.json");
            var ledger = new RedemptionLedger(ledgerPath);
            Check(ledger.TryReserve("42", "redemption-1") && !ledger.TryReserve("42", "redemption-1"));
            ledger.Finish("42", "redemption-1", "Displayed");
            Check(new RedemptionLedger(ledgerPath).Get("42", "redemption-1") == "Displayed" &&
                !new RedemptionLedger(ledgerPath).TryReserve("42", "redemption-1") &&
                new RedemptionLedger(ledgerPath).TryReserve("other", "redemption-1"));
            ledger.MarkExternalStatus("42", "redemption-2", "Canceled");
            Check(!new RedemptionLedger(ledgerPath).TryReserve("42", "redemption-2") &&
                new RedemptionLedger(ledgerPath).Get("42", "redemption-2") == "Canceled");
            Check(File.Exists(Path.Combine(directory, "redemptions", "42.json")) &&
                File.Exists(Path.Combine(directory, "redemptions", "other.json")));
            File.WriteAllText(ledgerPath, JsonData.Write(new Dictionary<string, string> {
                ["42:redemption-legacy"] = "Displayed" }));
            var migrated = new RedemptionLedger(ledgerPath);
            Check(migrated.Get("42", "redemption-legacy") == "Displayed" &&
                !migrated.TryReserve("42", "redemption-legacy") &&
                migrated.Get("42", "redemption-1") == "Displayed");
            File.WriteAllText(ledgerPath, "broken");
            var corrupt = new RedemptionLedger(ledgerPath);
            Check(corrupt.LoadFailed && !corrupt.TryReserve("42", "redemption-2"));
            Check(!JsonSerializer.Serialize(registry.Current).Contains("accessToken"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        Console.WriteLine("PASS: Channel Points parsing, scope, queue, reward mapping, persistent deduplication");
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Interactive check failed."); }
    private sealed class RewardHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Check(request.RequestUri!.Query.Contains("broadcaster_id=42") &&
                request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Contains("Client-Id"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"data":[{"id":"reward-1","image":{"url_1x":"https://static-cdn.jtvnw.net/icon.png"}},
                {"id":"reward-2","image":{"url_1x":"https://evil.example/x.png"}}]}
                """) });
        }
    }
}
