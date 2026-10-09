using System.Net;
using System.Net.Http;
using CS2TwitchCitizens.Twitch;

internal static class ChatSenderChecks
{
    public static async Task Run()
    {
        foreach (var pair in new[] {
            (HttpStatusCode.OK, "{\"data\":[{\"is_sent\":true}]}", ChatSendResult.Sent),
            (HttpStatusCode.OK, "{\"data\":[{\"is_sent\":false}]}", ChatSendResult.Dropped),
            (HttpStatusCode.Accepted, "{\"data\":[{\"is_sent\":true}]}", ChatSendResult.Sent),
            (HttpStatusCode.OK, "{\"data\":[]}", ChatSendResult.Dropped),
            (HttpStatusCode.InternalServerError, "{\"data\":[{\"is_sent\":true}]}", ChatSendResult.Failed),
            (HttpStatusCode.Unauthorized, "{}", ChatSendResult.Unauthorized),
            (HttpStatusCode.Forbidden, "{}", ChatSendResult.Forbidden),
            ((HttpStatusCode)429, "{}", ChatSendResult.RateLimited)
        })
        {
            var handler = new Handler(pair.Item1, pair.Item2);
            using var sender = new TwitchChatSender(new HttpClient(handler));
            var result = await sender.SendAsync("client", "token", "42", "42", "hello", "source-1", CancellationToken.None);
            if (result != pair.Item3 || handler.Calls != 1 || !handler.ValidRequest)
                throw new Exception("Chat send check failed: " + pair.Item3);
        }
        Console.WriteLine("PASS: Helix chat send, threading, 401/403/429 and dropped replies");
    }
    private sealed class Handler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public int Calls { get; private set; }
        public bool ValidRequest { get; private set; }
        public Handler(HttpStatusCode status, string body) { _status = status; _body = body; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            ValidRequest = request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == "https://api.twitch.tv/helix/chat/messages" &&
                request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Contains("Client-Id") &&
                json.Contains("\"reply_parent_message_id\":\"source-1\"") && json.Contains("\"message\":\"hello\"");
            return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        }
    }
}
