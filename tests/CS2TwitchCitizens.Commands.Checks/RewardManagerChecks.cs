using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.IO;
using System.Text;
using System.Text.Json;
using CS2TwitchCitizens.Twitch;

internal static class RewardManagerChecks
{
    public static async Task Run()
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        using var api = new TwitchRewardManager(http);
        var rewards = await api.ListAsync("client", "token", "42", false, CancellationToken.None);
        Check(rewards.Length == 1 && rewards[0].Title == "Notice");
        var managed = await api.ListAsync("client", "token", "42", true, CancellationToken.None);
        Check(managed.Length == 1);
        var edit = new TwitchRewardEdit { Title = "Notice", Cost = 250, Prompt = "Message",
            IsUserInputRequired = true, IsGlobalCooldownEnabled = true, GlobalCooldownSeconds = 60 };
        Check((await api.CreateAsync("client", "token", "42", edit, CancellationToken.None)).Id == "reward-1");
        Check((await api.UpdateAsync("client", "token", "42", "reward-1", edit, CancellationToken.None)).Cost == 250);
        await api.PatchAsync("client", "token", "42", "reward-1",
            new TwitchRewardPatch { Cost = 250 }, CancellationToken.None);
        var pending = await api.ListRedemptionsAsync("client", "token", "42", "reward-1", "UNFULFILLED", CancellationToken.None);
        Check(pending.Length == 1 && pending[0].Status == "UNFULFILLED");
        Check((await api.GetRedemptionAsync("client", "token", "42", "reward-1", "redeem-1", CancellationToken.None)).Id == "redeem-1");
        Check((await api.SetRedemptionStatusAsync("client", "token", "42", "reward-1", "redeem-1",
            "CANCELED", CancellationToken.None)).Status == "CANCELED");
        await api.DeleteAsync("client", "token", "42", "reward-1", CancellationToken.None);
        Check(handler.Requests.Count == 9);
        Check(handler.Requests[2].Body!.Contains("\"cost\":250") &&
            handler.Requests[2].Body!.Contains("\"is_user_input_required\":true"));
        Check(handler.Requests[4].Body == "{\"cost\":250}" && handler.Requests[4].Method.Method == "PATCH");
        Check(handler.Requests[2].Uri.Query == "?broadcaster_id=42");
        Check(handler.Requests[5].Uri.AbsolutePath.EndsWith("/custom_rewards/redemptions") &&
            handler.Requests[5].Uri.Query.Contains("reward_id=reward-1"));
        Check(handler.Requests[7].Body!.Contains("\"status\":\"CANCELED\""));
        Check(handler.Requests.All(r => r.ClientId == "client" && r.Authorization == "Bearer token"));

        var failed = new Handler { FailCreate = true };
        using var failedApi = new TwitchRewardManager(new HttpClient(failed));
        await Expect<TwitchRewardException>(() => failedApi.CreateAsync("client", "token", "42", edit,
            CancellationToken.None));
        Check(failed.Requests.Count == 1); // No blind POST retry.
        var retry = new Handler { RateLimitOnce = true };
        using var retryApi = new TwitchRewardManager(new HttpClient(retry));
        Check((await retryApi.ListAsync("client", "token", "42", false, CancellationToken.None)).Length == 1 &&
            retry.Requests.Count == 2);
        var paged = new Handler { PagedRedemptions = true };
        using var pagedApi = new TwitchRewardManager(new HttpClient(paged));
        var twoPages = await pagedApi.ListRedemptionsAsync("client", "token", "42", "reward-1",
            "UNFULFILLED", CancellationToken.None);
        Check(twoPages.Length == 2 && twoPages[1].Id == "redeem-2" &&
            paged.Requests[1].Uri.Query.Contains("after=next"));
        await Expect<ArgumentException>(() => failedApi.CreateAsync("client", "token", "42",
            new TwitchRewardEdit { Title = "", Cost = 0 }, CancellationToken.None));
        await Expect<ArgumentException>(() => failedApi.SetRedemptionStatusAsync("client", "token", "42",
            "reward", "redemption", "UNFULFILLED", CancellationToken.None));
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden,
            (HttpStatusCode)429, HttpStatusCode.InternalServerError })
        {
            var rejected = new Handler { FailureStatus = status };
            using var rejectedApi = new TwitchRewardManager(new HttpClient(rejected));
            try { await rejectedApi.ListAsync("client", "token", "42", false, CancellationToken.None);
                throw new Exception("Expected Twitch reward HTTP error."); }
            catch (TwitchRewardException ex) { Check(ex.StatusCode == (int)status); }
            Check(rejected.Requests.Count == ((int)status == 429 || (int)status >= 500 ? 2 : 1));
        }
        var timedOut = new Handler { Timeout = true };
        using var timedOutApi = new TwitchRewardManager(new HttpClient(timedOut));
        await Expect<TaskCanceledException>(() => timedOutApi.ListAsync("client", "token", "42", false,
            CancellationToken.None));
        Check(timedOut.Requests.Count == 1);
        var logs = new List<string>();
        var empty = new Handler { EmptyRewards = true };
        using (var loggedApi = new TwitchRewardManager(new HttpClient(empty), logs.Add))
        {
            Check((await loggedApi.ListAsync("client", "secret-token", "42", false, CancellationToken.None)).Length == 0);
            Check((await loggedApi.ListAsync("client", "secret-token", "42", true, CancellationToken.None)).Length == 0);
        }
        Check(empty.Requests.Count == 2 && logs.Any(x => x.Contains("operation=ListAll result=start")) &&
            logs.Any(x => x.Contains("operation=ListManageable result=start")) &&
            logs.Count(x => x.Contains("rewards=0")) == 2);
        logs.Clear();
        var apiError = new Handler { FailureStatus = HttpStatusCode.Forbidden,
            FailureBody = "{\"message\":\"Missing required scope for user secret-person\"}" };
        using (var loggedApi = new TwitchRewardManager(new HttpClient(apiError), logs.Add))
            await Expect<TwitchRewardException>(() => loggedApi.ListAsync("client", "secret-token", "42", false,
                CancellationToken.None));
        Check(logs.Any(x => x.Contains("status=403 api=missing_scope")) &&
            logs.All(x => !x.Contains("secret-token") && !x.Contains("secret-person")));
        logs.Clear();
        var badJson = new Handler { InvalidJson = true };
        using (var loggedApi = new TwitchRewardManager(new HttpClient(badJson), logs.Add))
            await Expect<Exception>(() => loggedApi.ListAsync("client", "secret-token", "42", false,
                CancellationToken.None));
        Check(logs.Any(x => x.Contains("reason=deserialization")));
        Check(TwitchRewardManager.SafeExceptionReason(new HttpRequestException("private", new AuthenticationException("private"))) == "tls");
        Check(TwitchRewardManager.SafeExceptionReason(new HttpRequestException("private", new SocketException((int)SocketError.HostNotFound))) == "dns");
        Check(TwitchRewardManager.SafeExceptionReason(new HttpRequestException("private",
            new WebException("private", WebExceptionStatus.TrustFailure))) == "tls");
        Check(TwitchRewardManager.SafeExceptionReason(new WebException("private",
            WebExceptionStatus.NameResolutionFailure)) == "dns");
        Check(TwitchRewardManager.SafeExceptionReason(new TaskCanceledException()) == "timeout");
        Check(TwitchRewardManager.SafeExceptionReason(new IOException("private")) == "file_storage");
        Check(TwitchRewardManager.SafeExceptionReason(new IOException("private"), networkRequest: true) == "http_transport");
        logs.Clear();
        var created = new Handler();
        using (var loggedApi = new TwitchRewardManager(new HttpClient(created), logs.Add))
        {
            await loggedApi.CreateAsync("client", "secret-token", "42", edit, CancellationToken.None);
            await loggedApi.PatchAsync("client", "secret-token", "42", "reward-1",
                new TwitchRewardPatch { Cost = 300 }, CancellationToken.None);
        }
        Check(created.Requests.Count == 2 && logs.Any(x => x.Contains("operation=Create result=start")) &&
            logs.Any(x => x.Contains("operation=Patch result=start")) &&
            logs.All(x => !x.Contains("secret-token") && !x.Contains("Message")));
        using (var brokenLoggerApi = new TwitchRewardManager(new HttpClient(new Handler()),
            _ => throw new IOException("logger unavailable")))
            Check((await brokenLoggerApi.ListAsync("client", "token", "42", false,
                CancellationToken.None)).Length == 1);
        Console.WriteLine("PASS: Helix reward CRUD, permissions headers, request fields, redemptions, no POST retry");
    }
    private static void Check(bool value) { if (!value) throw new Exception("Reward manager check failed."); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private sealed record Request(HttpMethod Method, Uri Uri, string? Body, string ClientId, string Authorization);
    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<Request> Requests = new();
        public bool FailCreate;
        public bool RateLimitOnce;
        public bool PagedRedemptions;
        public HttpStatusCode? FailureStatus;
        public bool Timeout;
        public bool EmptyRewards;
        public bool InvalidJson;
        public string? FailureBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Request(request.Method, request.RequestUri!, body,
                request.Headers.GetValues("Client-Id").Single(), request.Headers.Authorization!.ToString()));
            if (Timeout) throw new TaskCanceledException();
            if (FailureStatus.HasValue) return new HttpResponseMessage(FailureStatus.Value)
                { Content = new StringContent(FailureBody ?? "{}") };
            if (FailCreate) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (RateLimitOnce && Requests.Count == 1)
            {
                var limited = new HttpResponseMessage((HttpStatusCode)429);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return limited;
            }
            if (request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);
            var redemption = request.RequestUri!.AbsolutePath.EndsWith("/redemptions");
            string json;
            if (redemption)
            {
                var status = body == null ? "UNFULFILLED" : JsonDocument.Parse(body).RootElement.GetProperty("status").GetString();
                var id = PagedRedemptions && request.RequestUri.Query.Contains("after=next") ? "redeem-2" : "redeem-1";
                json = "{\"data\":[{\"id\":\"" + id + "\",\"broadcaster_id\":\"42\",\"status\":\"" + status +
                    "\",\"reward\":{\"id\":\"reward-1\"}}]" +
                    (PagedRedemptions && id == "redeem-1" ? ",\"pagination\":{\"cursor\":\"next\"}" : "") + "}";
            }
            else json = InvalidJson ? "{" : EmptyRewards ? "{\"data\":[]}" :
                "{\"data\":[{\"id\":\"reward-1\",\"title\":\"Notice\",\"cost\":250}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
