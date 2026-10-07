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

static void Check(bool condition)
{
    if (!condition) throw new Exception("Command check failed.");
}
