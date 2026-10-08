using CS2TwitchCitizens.Commands;
using CS2TwitchCitizens.Twitch;
using System.Text.Json;

var now = DateTimeOffset.Parse("2026-10-04T00:00:00+03:00");
foreach (var verb in new[] { "!join", "!me", "!find", "!history" })
{
    Check(TwitchCommandParser.TryParse($"  {verb.ToUpperInvariant()}  argument  ",
        "12345", "login", "Display", now, out var parsed));
    Check(parsed is not null && parsed.Command == verb && parsed.Arguments == "argument");
    Check(parsed!.TwitchUserId == "12345" && parsed.ReceivedAt == now);
}

Check(!TwitchCommandParser.TryParse("!joined", "12345", "", "", now, out _));
Check(!TwitchCommandParser.TryParse("!unknown", "12345", "", "", now, out _));
Check(!TwitchCommandParser.TryParse("!join", " ", "", "", now, out _));
Check(!TwitchCommandParser.TryParse(null, "12345", "", "", now, out _));

var queue = new TwitchCommandQueue();
DevCommandInjector.EnqueueSampleJoin(queue, now);
TwitchCommand? dev = null;
Check(TwitchCommandReceiver.Drain(queue, 5, command => dev = command) == 1);
Check(dev is not null && dev.Command == "!join" && dev.TwitchUserId == "dev-user-1");
Check(!queue.TryDequeue(out _));

Parallel.For(0, 100, i =>
    queue.Enqueue(new TwitchCommand(i.ToString(), "", "", "!join", "", now)));
var seen = new HashSet<string>();
while (queue.TryDequeue(out var item))
    Check(item is not null && seen.Add(item.TwitchUserId));
Check(seen.Count == 100);

Console.WriteLine("PASS: parser, DEV command handoff, concurrent queue");

var bindings = new ViewerBindingRegistry<int>();
Check(bindings.GetStatus("missing", _ => true, out _) == BindingStatus.NotJoined);
Check(bindings.Join("viewer-1", 101, out var first) == JoinResult.Joined);
Check(first.CitizenKey == 101 && bindings.IsCitizenBound(101));
Check(bindings.Join("viewer-1", 102, out var repeated) == JoinResult.AlreadyJoined);
Check(repeated.CitizenKey == 101 && !bindings.IsCitizenBound(102));
Check(bindings.Join("viewer-2", 101, out _) == JoinResult.CitizenInUse);
Check(bindings.GetStatus("viewer-1", key => key == 101, out _) == BindingStatus.Active);
Check(bindings.GetStatus("viewer-1", _ => false, out var stale) == BindingStatus.Stale);
Check(stale!.CitizenKey == 101 && bindings.IsCitizenBound(101));

var sequence = new TwitchCommandQueue();
DevCommandInjector.EnqueueSampleSequence(sequence, now);
var sequenceBindings = new ViewerBindingRegistry<int>();
var outcomes = new List<string>();
void Process(TwitchCommand command)
{
    if (command.Command == "!join")
        outcomes.Add(sequenceBindings.Join(command.TwitchUserId, 501, out _).ToString());
    else if (command.Command == "!me")
        outcomes.Add(sequenceBindings.GetStatus(command.TwitchUserId, _ => true, out _).ToString());
}
Check(TwitchCommandReceiver.Drain(sequence, 2, Process) == 2 && sequence.Count == 1);
Check(TwitchCommandReceiver.Drain(sequence, 2, Process) == 1 && sequence.Count == 0);
Check(outcomes.SequenceEqual(new[] { "Joined", "Active", "AlreadyJoined" }));
Console.WriteLine("PASS: session bindings, stale detection, DEV sequence");

static string ChatEnvelope(string messageId, string text) =>
    "{\"metadata\":{\"message_id\":\"" + messageId + "\",\"message_type\":\"notification\"}," +
    "\"payload\":{\"subscription\":{\"type\":\"channel.chat.message\"}," +
    "\"event\":{\"broadcaster_user_id\":\"42\",\"chatter_user_id\":\"9001\"," +
    "\"chatter_user_login\":\"viewer_login\",\"chatter_user_name\":\"Viewer_Name\"," +
    "\"message\":{\"text\":\"" + text + "\"}}}}";

Check(!EventSubProtocol.TryRead("{bad", out _));
Check(EventSubProtocol.TryRead(ChatEnvelope("normal", "hello"), out var ordinary));
Check(!EventSubProtocol.TryGetCommand(ordinary!, "42", now, out _));
Check(EventSubProtocol.TryRead(ChatEnvelope("join-1", "!join"), out var joinEnvelope));
Check(!EventSubProtocol.TryGetCommand(joinEnvelope!, "wrong-channel", now, out _));
Check(EventSubProtocol.TryGetCommand(joinEnvelope!, "42", now, out var twitchJoin));
Check(twitchJoin!.TwitchUserId == "9001" && twitchJoin.Login == "viewer_login" &&
      twitchJoin.DisplayName == "Viewer_Name" && twitchJoin.Command == "!join");
Check(EventSubProtocol.TryRead(ChatEnvelope("me-1", "!me"), out var meEnvelope));
Check(EventSubProtocol.TryGetCommand(meEnvelope!, "42", now, out var twitchMe));
Check(twitchMe!.TwitchUserId == "9001" && twitchMe.Command == "!me");
Check(EventSubProtocol.TryRead(ChatEnvelope("find-1", "!find"), out var findEnvelope));
Check(EventSubProtocol.TryGetCommand(findEnvelope!, "42", now, out var twitchFind));
Check(twitchFind!.TwitchUserId == "9001" && twitchFind.Command == "!find");

var incoming = new TwitchCommandQueue();
incoming.Enqueue(twitchJoin);
incoming.Enqueue(twitchMe);
var handled = new List<TwitchCommand>();
Check(TwitchCommandReceiver.Drain(incoming, 1, handled.Add) == 1 && incoming.Count == 1);
Check(TwitchCommandReceiver.Drain(incoming, 1, handled.Add) == 1 && incoming.Count == 0);
Check(handled[0].Command == "!join" && handled[1].Command == "!me");

var messageIds = new EventSubMessageIds(2);
Check(messageIds.TryAdd("one") && !messageIds.TryAdd("one"));
Check(messageIds.TryAdd("two") && messageIds.TryAdd("three"));
Check(messageIds.TryAdd("one") && !messageIds.TryAdd(null));
Check(EventSubRetryPolicy.Delay(1) == TimeSpan.FromSeconds(2));
Check(EventSubRetryPolicy.Delay(2) == TimeSpan.FromSeconds(4));
Check(EventSubRetryPolicy.Delay(100) == TimeSpan.FromSeconds(64));
Check(EventSubProtocol.TryRead("{\"metadata\":{\"message_type\":\"session_reconnect\"},\"payload\":{\"session\":{\"reconnect_url\":\"wss://eventsub.wss.twitch.tv/ws?keepalive_timeout_seconds=30\"}}}", out var reconnect));
Check(EventSubProtocol.TryGetReconnectUri(reconnect!, out var reconnectUri) && reconnectUri!.Scheme == "wss");
Check(EventSubProtocol.TryRead("{\"metadata\":{\"message_type\":\"session_reconnect\"},\"payload\":{\"session\":{\"reconnect_url\":\"https://example.com/ws\"}}}", out var invalidReconnect));
Check(!EventSubProtocol.TryGetReconnectUri(invalidReconnect!, out _));
Console.WriteLine("PASS: EventSub payload, identity, queue, dedup, reconnect delay");

var findBindings = new ViewerBindingRegistry<int>();
var missingFind = CitizenLookup.Find(findBindings, "absent", _ => true, _ => new CitizenPosition(1, 2, 3));
Check(missingFind.Status == CitizenFindStatus.NotJoined);
Check(findBindings.Join("viewer-a", 71, out _) == JoinResult.Joined);
var found = CitizenLookup.Find(findBindings, "viewer-a", key => key == 71, _ => new CitizenPosition(1, 2, 3));
Check(found.Status == CitizenFindStatus.Found && found.CitizenKey == 71 && found.Position!.Value.Y == 2);
Check(CitizenLookup.Find(findBindings, "viewer-a", _ => false, _ => new CitizenPosition(1, 2, 3)).Status == CitizenFindStatus.Stale);
Check(CitizenLookup.Find(findBindings, "viewer-a", _ => true, _ => null).Status == CitizenFindStatus.PositionUnavailable);
Check(CitizenLookup.Find(findBindings, "viewer-a", _ => true, _ => new CitizenPosition(1, 2, 3)).Status == CitizenFindStatus.Found);
Check(findBindings.GetAllBindings().Count == 1);
Check(findBindings.Join("viewer-b", 71, out _) == JoinResult.CitizenInUse);
Check(typeof(CitizenLookup).Assembly.GetReferencedAssemblies().All(a =>
    a.Name != "Game" && a.Name != "Unity.Entities"));
Check(typeof(ViewerCitizenInfo).GetProperties().All(p =>
    !(p.PropertyType.FullName ?? string.Empty).Contains("Unity.Entities") &&
    !(p.PropertyType.FullName ?? string.Empty).Contains("EntityManager") &&
    !(p.PropertyType.FullName ?? string.Empty).Contains("World")));
Console.WriteLine("PASS: find lookup, stale and missing bindings, no automatic camera focus, safe UI DTO");

var panel = ViewerPanelSnapshot.Create("Connected", "channel-42", true, new[] {
    new ViewerCitizenInfo { TwitchUserId = "viewer-1", Login = "first", DisplayName = "First",
        CitizenName = "first", Age = "Adult", Home = "Entity(1:1)", IsValid = true,
        PositionAvailable = true, CurrentLifeId = "life-2", TotalLives = 2,
        CurrentLifeStatus = "Active", PreviousLives = new[] {
            new ViewerLifeInfo { LifeId = "life-1", Status = "Deceased", LastKnownAge = "Elderly" }
        } },
    new ViewerCitizenInfo { TwitchUserId = "viewer-2", Login = "lost", IsValid = false }
});
using (var json = JsonDocument.Parse(panel.ToJson()))
{
    var root = json.RootElement;
    Check(root.GetProperty("twitchStatus").GetString() == "Connected");
    Check(root.GetProperty("channelId").GetString() == "channel-42");
    Check(root.GetProperty("viewers").GetArrayLength() == 2);
    Check(root.GetProperty("viewers")[0].GetProperty("hasHome").GetBoolean());
    Check(!root.GetProperty("viewers")[1].GetProperty("isValid").GetBoolean());
    Check(root.GetProperty("viewers")[0].GetProperty("totalLives").GetInt32() == 2);
    Check(root.GetProperty("viewers")[0].GetProperty("previousLives")[0].GetProperty("status").GetString() == "Deceased");
    Check(!panel.ToJson().Contains("Entity(") && !panel.ToJson().Contains("accessToken") &&
          !panel.ToJson().Contains("clientSecret"));
}
string? actionId = null;
Check(ViewerPanelActions.Focus("viewer-1", id => { actionId = id; return CitizenFocusStatus.FocusRequested; }) == CitizenFocusStatus.FocusRequested);
Check(actionId == "viewer-1");
Check(ViewerPanelActions.Focus(" ", _ => throw new Exception("Empty ID reached camera")) == CitizenFocusStatus.CitizenNotFound);
using (var feedback = JsonDocument.Parse(new ViewerFocusFeedback {
    ViewerId = "viewer-1", Result = "FocusRequested", Sequence = 2 }.ToJson()))
{
    Check(feedback.RootElement.GetProperty("viewerId").GetString() == "viewer-1");
    Check(feedback.RootElement.GetProperty("result").GetString() == "FocusRequested");
    Check(feedback.RootElement.GetProperty("sequence").GetInt32() == 2);
}
Console.WriteLine("PASS: UI snapshot serialization, stale rows, safe focus handoff, no secrets or raw entities");

var lives = new ViewerLifeJournal<int>();
Check(lives.TryStart("viewer-a", "alice", "Alice", 71, "2026-01-01", "label:Old Name",
    "Adult", "home available", "workplace available", out var firstLife));
Check(firstLife is not null && firstLife.Status == ViewerLifeStatus.Active);
Check(!lives.TryStart("viewer-a", "alice", "Alice", 72, "2026-01-02", "",
    "Adult", "", "", out _));
Check(!lives.TryStart("viewer-b", "bob", "Bob", 71, "2026-01-02", "",
    "Adult", "", "", out _));
Check(lives.TryStart("viewer-b", "bob", "Bob", 72, "2026-01-02", "",
    "Adult", "", "", out _));
lives.UpdateSnapshot("viewer-a", "Elderly", "home available", "");
Check(lives.MarkDeceased("viewer-a", "2026-02-01", ""));
Check(!lives.IsClaimed(71) && firstLife!.Status == ViewerLifeStatus.Deceased &&
      firstLife.CitizenKey == 0 && firstLife.LastKnownAge == "Elderly");
Check(lives.TryStart("viewer-a", "alice", "Alice", 73, "2026-02-02", "label:Next Name",
    "Adult", "", "", out var secondLife));
Check(secondLife!.LifeId != firstLife!.LifeId && lives.IsClaimed(73));
Check(lives.MarkMissing("viewer-b"));
Check(!lives.TryStart("viewer-b", "bob", "Bob", 74, "2026-02-03", "",
    "Adult", "", "", out _));
Check(lives.TryGet("viewer-b", out var missingAccount) &&
      missingAccount!.Current!.Status == ViewerLifeStatus.Missing);
var savedModel = JsonSerializer.Serialize(lives.Accounts);
Check(!savedModel.Contains("accessToken") && !savedModel.Contains("clientSecret"));
var restoredAccounts = JsonSerializer.Deserialize<List<ViewerLifeAccount<int>>>(savedModel)!;
var restored = new ViewerLifeJournal<int>();
restored.Restore(restoredAccounts, key => key == 73);
Check(restored.TryGet("viewer-a", out var restoredAlice) && restoredAlice!.Lives.Count == 2 &&
      restoredAlice.Current!.Status == ViewerLifeStatus.Active && restoredAlice.Current.CitizenKey == 73 &&
      restoredAlice.Lives[0].Status == ViewerLifeStatus.Deceased);
Check(restored.TryGet("viewer-b", out var restoredBob) &&
      restoredBob!.Current!.Status == ViewerLifeStatus.Missing);
var orphaned = JsonSerializer.Deserialize<List<ViewerLifeAccount<int>>>(savedModel)!;
var missingAfterLoad = new ViewerLifeJournal<int>();
missingAfterLoad.Restore(orphaned, _ => false);
Check(missingAfterLoad.TryGet("viewer-a", out var orphanedAlice) &&
      orphanedAlice!.Current!.Status == ViewerLifeStatus.Missing &&
      !missingAfterLoad.IsClaimed(73));
var nextCity = new ViewerLifeJournal<int>();
nextCity.Restore(Array.Empty<ViewerLifeAccount<int>>(), _ => true);
Check(nextCity.Accounts.Count == 0 && restored.Accounts.Count == 2);
var invalidVersionRejected = false;
try { ViewerLifeSaveFormat.RequireSupported(99); }
catch (System.IO.InvalidDataException) { invalidVersionRejected = true; }
Check(invalidVersionRejected && ViewerLifeSaveFormat.Version == 1);
Check(EventSubProtocol.TryRead(ChatEnvelope("history-1", "!history"), out var historyEnvelope) &&
      EventSubProtocol.TryGetCommand(historyEnvelope!, "42", now, out var historyCommand) &&
      historyCommand!.Command == "!history" && historyCommand.TwitchUserId == "9001");
Console.WriteLine("PASS: life transitions, duplicate protection, model roundtrip, city isolation, version guard, history command");

static void Check(bool condition)
{
    if (!condition) throw new Exception("Command check failed.");
}
