using System.Net;
using System.Net.Http;
using System.Text;
using CS2TwitchCitizens.Twitch;

internal static class OAuthChecks
{
    public static async Task Run()
    {
        var handler = new MockHandler();
        using var http = new HttpClient(handler);
        using var oauth = new TwitchOAuthClient(http);
        var code = await oauth.RequestDeviceAsync("public-client", CancellationToken.None);
        Check(code.Interval == 5 && code.ExpiresIn == 120 && code.UserCode == "ABCD");
        Check(await oauth.PollAsync("public-client", code.DeviceCode, CancellationToken.None) == null);
        var token = await oauth.PollAsync("public-client", code.DeviceCode, CancellationToken.None);
        Check(token?.AccessToken == "access-1" && token?.RefreshToken == "refresh-1");
        var identity = await oauth.ValidateAsync("public-client", token!.AccessToken, CancellationToken.None);
        Check(identity.UserId == "42" && identity.Login == "streamer");
        Check(await oauth.GetDisplayNameAsync("public-client", token.AccessToken, CancellationToken.None) == "Streamer");
        var refreshed = await oauth.RefreshAsync("public-client", token.RefreshToken, CancellationToken.None);
        Check(refreshed.AccessToken == "access-2" && refreshed.RefreshToken == "refresh-2");
        await oauth.RevokeAsync("public-client", refreshed.AccessToken, CancellationToken.None);
        Check(handler.Requests == 7);
        await Expect<UnauthorizedAccessException>(async () => await new TwitchOAuthClient(new HttpClient(new ErrorHandler("access_denied")))
            .PollAsync("public-client", "device", CancellationToken.None));
        await Expect<TimeoutException>(async () => await new TwitchOAuthClient(new HttpClient(new ErrorHandler("expired_token")))
            .PollAsync("public-client", "device", CancellationToken.None));
        await Expect<TwitchRateLimitException>(async () => await new TwitchOAuthClient(new HttpClient(new ErrorHandler("slow_down")))
            .PollAsync("public-client", "device", CancellationToken.None));
        await Expect<UnauthorizedAccessException>(async () => await new TwitchOAuthClient(new HttpClient(new ErrorHandler("revoked", HttpStatusCode.Unauthorized)))
            .RefreshAsync("public-client", "refresh", CancellationToken.None));
        await Expect<HttpRequestException>(async () => await new TwitchOAuthClient(new HttpClient(new ErrorHandler("network", HttpStatusCode.ServiceUnavailable)))
            .RequestDeviceAsync("public-client", CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Expect<OperationCanceledException>(async () => await oauth.RequestDeviceAsync("public-client", cancelled.Token));

        var view = new TwitchAuthView { State = "Pending", UserCode = "ABCD", VerificationUri = "https://www.twitch.tv/activate",
            Login = "streamer" }.ToJson();
        Check(!view.Contains("access-1") && !view.Contains("refresh-1") && !view.Contains("device-1"));

        var path = Path.Combine(Path.GetTempPath(), "cs2twitchcitizens-check-" + Guid.NewGuid().ToString("N"), "credentials.dpapi");
        var store = new TwitchCredentialStore(path);
        try
        {
            store.Save(new TwitchCredentials { AccessToken = "access-1", RefreshToken = "refresh-1", UserId = "42" });
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("refresh-1"));
            Check(store.Load()?.RefreshToken == "refresh-1");
            store.Save(new TwitchCredentials { AccessToken = "access-2", RefreshToken = "refresh-2", UserId = "42" });
            Check(store.Load()?.RefreshToken == "refresh-2");
            store.Delete();
            Check(store.Load() == null);
        }
        finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!, true); }
        await Expect<Exception>(() => { new TwitchCredentialStore(Path.GetTempPath()).Save(new TwitchCredentials()); return Task.CompletedTask; });
        Console.WriteLine("PASS: OAuth device, pending, validation, refresh rotation, revoke, DPAPI, UI secret isolation");
    }

    private static void Check(bool condition) { if (!condition) throw new Exception("OAuth check failed."); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected OAuth error: " + typeof(T).Name);
    }

    private sealed class ErrorHandler : HttpMessageHandler
    {
        private readonly string _message;
        private readonly HttpStatusCode _status;
        public ErrorHandler(string message, HttpStatusCode status = HttpStatusCode.BadRequest)
        { _message = message; _status = status; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_status) {
                Content = new StringContent("{\"message\":\"" + _message + "\"}") });
        }
    }

    private sealed class MockHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            string json;
            HttpStatusCode status = HttpStatusCode.OK;
            if (path.EndsWith("/device"))
            {
                Check(body.Contains("scopes=user%3Aread%3Achat") && body.Contains("client_id=public-client"));
                json = "{\"device_code\":\"device-1\",\"user_code\":\"ABCD\",\"verification_uri\":\"https://www.twitch.tv/activate\",\"expires_in\":120,\"interval\":5}";
            }
            else if (path.EndsWith("/token") && body.Contains("device_code"))
            {
                Check(body.Contains("urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code"));
                if (Requests == 2) { status = HttpStatusCode.BadRequest; json = "{\"message\":\"authorization_pending\"}"; }
                else json = "{\"access_token\":\"access-1\",\"refresh_token\":\"refresh-1\",\"expires_in\":14400}";
            }
            else if (path.EndsWith("/validate"))
            {
                Check(request.Headers.Authorization?.Scheme == "OAuth");
                json = "{\"client_id\":\"public-client\",\"user_id\":\"42\",\"login\":\"streamer\",\"scopes\":[\"user:read:chat\"]}";
            }
            else if (path.EndsWith("/users"))
            {
                Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Contains("Client-Id"));
                json = "{\"data\":[{\"display_name\":\"Streamer\"}]}";
            }
            else if (path.EndsWith("/token") && body.Contains("refresh_token"))
            {
                Check(body.Contains("refresh_token=refresh-1"));
                json = "{\"access_token\":\"access-2\",\"refresh_token\":\"refresh-2\",\"expires_in\":14400}";
            }
            else { Check(path.EndsWith("/revoke") && body.Contains("token=access-2")); json = "{}"; }
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
