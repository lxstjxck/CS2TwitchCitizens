using CS2TwitchCitizens.Commands;
using CS2TwitchCitizens.Twitch;

var now = DateTimeOffset.Parse("2026-10-04T00:00:00+03:00");
foreach (var verb in new[] { "!join", "!me", "!find" })
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
Check(!EventSubProtocol.TryGetCommand(findEnvelope!, "42", now, out _));

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

static void Check(bool condition)
{
    if (!condition) throw new Exception("Command check failed.");
}
