using CS2TwitchCitizens.Commands;

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

static void Check(bool condition)
{
    if (!condition) throw new Exception("Command check failed.");
}
