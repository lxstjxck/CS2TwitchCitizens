using System.Collections;
using System.Reflection;
using CS2TwitchCitizens.Commands;
using CS2TwitchCitizens.Twitch;

internal static class ReplyQueueChecks
{
    public static void Run()
    {
        var messages = new List<string>();
        var queue = new TwitchCommandQueue();
        using var oauth = new TwitchOAuthClient(new HttpClient());
        using var controller = new TwitchConnectionController("client", new TwitchCredentialStore("unused"),
            oauth, queue, messages.Add, false);
        var credentials = new TwitchCredentials { UserId = "broadcaster", AccessToken = "secret" };
        var service = new TwitchEventSubService(new TwitchConfig { Enabled = true, ClientId = "client",
            AccessToken = "secret", BroadcasterUserId = "broadcaster", UserId = "broadcaster" }, queue, messages.Add);
        Set(service, "_status", TwitchConnectionStatus.Connected);
        Set(controller, "_credentials", credentials);
        Set(controller, "_eventSub", service);
        Set(controller, "_view", new TwitchAuthView { State = "Connected", WritePermission = "Allowed" });

        var own = new TwitchCommand("broadcaster", "", "", "!me", "", DateTimeOffset.UtcNow, "own-message");
        if (!controller.QueueReply(own, "A citizen reply")) throw new Exception("Broadcaster reply was suppressed.");
        var other = new TwitchCommand("viewer", "", "", "!find", "", DateTimeOffset.UtcNow, "viewer-message");
        if (!controller.QueueReply(other, "Another reply")) throw new Exception("Viewer reply was suppressed.");
        if (((ICollection)Get(controller, "_replies")).Count != 2) throw new Exception("Reply queue count mismatch.");

        Set(controller, "_view", new TwitchAuthView { State = "Connected", WritePermission = "AuthorizationRequired" });
        if (controller.QueueReply(own, "Blocked reply") ||
            !messages.Any(line => line.Contains("write-permission-unavailable")) ||
            messages.Any(line => line.Contains("secret") || line.Contains("Blocked reply")))
            throw new Exception("Missing write scope was not safely logged.");
        Console.WriteLine("PASS: broadcaster replies, viewer replies, missing-scope queue guard and safe logging");
    }

    private static object Get(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
