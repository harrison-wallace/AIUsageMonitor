using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AIUsageMonitor.Collectors;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Tests;

// GitHub Copilot quotas from copilot_internal/user. No network: a scripted handler and token.
[TestClass]
public sealed class GitHubCopilotUsageCollectorTests
{
    // The shape GitHub returned for a Copilot Free account (identifiers removed).
    private const string FreeAccount = """
        {
          "access_type_sku": "free_limited_copilot",
          "copilot_plan": "individual",
          "quota_snapshots": {
            "chat": { "entitlement": 200, "remaining": 150, "quota_remaining": 150.0, "unlimited": false, "overage_count": 0 },
            "completions": { "entitlement": 2000, "remaining": 2000, "unlimited": false, "overage_count": 0 },
            "premium_interactions": { "entitlement": 0, "remaining": 0, "unlimited": false, "overage_count": 0 }
          },
          "quota_reset_date_utc": "2026-12-01T00:00:00.000Z"
        }
        """;

    private const string ProAccount = """
        {
          "access_type_sku": "monthly_subscriber",
          "copilot_plan": "individual",
          "quota_snapshots": {
            "chat": { "unlimited": true, "entitlement": 0, "remaining": 0 },
            "completions": { "unlimited": true, "entitlement": 0, "remaining": 0 },
            "premium_interactions": { "entitlement": 300, "remaining": 0, "unlimited": false, "overage_count": 12 }
          },
          "quota_reset_date": "2026-12-01"
        }
        """;

    [TestMethod]
    public void FreePlanShowsChatAndCompletionsAndMarksPremiumNotOnPlan()
    {
        using var document = JsonDocument.Parse(FreeAccount);
        var usage = GitHubCopilotUsageCollector.Parse(document.RootElement);

        Assert.AreEqual(KnownProviders.GitHubCopilot, usage.Name);
        Assert.AreEqual("Free", usage.PlanName);
        Assert.IsFalse(usage.IsUnavailable);
        var premium = usage.Windows.Single(window => window.Title == "Premium requests");
        Assert.IsTrue(premium.IsInactive, "Free has no premium requests.");
        var chat = usage.Windows.Single(window => window.Title == "Chat");
        Assert.AreEqual(200, chat.Limit);
        Assert.AreEqual(50, chat.Used);
        Assert.AreEqual(25, chat.UsedPercent);
        Assert.AreEqual(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), chat.ResetAt);
        Assert.AreEqual("50 of 200 used this month", chat.Detail);
    }

    [TestMethod]
    public void ProPlanShowsPremiumOverageAndUnlimitedQuotas()
    {
        using var document = JsonDocument.Parse(ProAccount);
        var usage = GitHubCopilotUsageCollector.Parse(document.RootElement);

        Assert.AreEqual("Pro", usage.PlanName);
        var premium = usage.Windows.Single(window => window.Title == "Premium requests");
        Assert.AreEqual(100, premium.UsedPercent);
        Assert.AreEqual("300 of 300 used this month, 12 over", premium.Detail);
        Assert.IsTrue(usage.Windows.Single(window => window.Title == "Chat").IsInactive);
        Assert.AreEqual("Unlimited", usage.Windows.Single(window => window.Title == "Completions").Detail);
    }

    [TestMethod]
    [DataRow("free_limited_copilot", "individual", "Free")]
    [DataRow("monthly_subscriber", "individual", "Pro")]
    [DataRow("copilot_pro_plus", "individual", "Pro+")]
    [DataRow("copilot_for_business_seat", "business", "Business")]
    [DataRow("", "enterprise", "Enterprise")]
    public void PlanNamesAreShort(string sku, string plan, string expected)
    {
        Assert.AreEqual(expected, GitHubCopilotUsageCollector.FormatPlan(sku, plan));
    }

    [TestMethod]
    public async Task CollectReadsTheUserEndpointWithTheToken()
    {
        HttpRequestMessage? seen = null;
        var collector = new GitHubCopilotUsageCollector(
            new HttpClient(new ScriptedHttpHandler(request =>
            {
                seen = request;
                return Json(HttpStatusCode.OK, FreeAccount);
            })),
            _ => Task.FromResult<string?>("test-token"));

        var usage = await collector.CollectAsync(CancellationToken.None);

        Assert.IsFalse(usage.IsUnavailable, usage.StatusMessage);
        Assert.AreEqual("https://api.github.com/copilot_internal/user", seen!.RequestUri!.ToString());
        Assert.AreEqual("token", seen.Headers.Authorization!.Scheme);
        Assert.AreEqual(HttpMethod.Get, seen.Method);
    }

    [TestMethod]
    public async Task MissingLoginAndRefusedTokenAreUnavailable()
    {
        var noLogin = new GitHubCopilotUsageCollector(
            new HttpClient(new ScriptedHttpHandler(_ => throw new InvalidOperationException("No request without a token."))),
            _ => Task.FromResult<string?>(null));
        var refused = new GitHubCopilotUsageCollector(
            new HttpClient(new ScriptedHttpHandler(_ => Json(HttpStatusCode.Unauthorized, "{}"))),
            _ => Task.FromResult<string?>("expired"));

        var first = await noLogin.CollectAsync(CancellationToken.None);
        var second = await refused.CollectAsync(CancellationToken.None);

        Assert.IsTrue(first.IsUnavailable);
        StringAssert.Contains(first.StatusMessage, "gh auth login");
        Assert.IsTrue(second.IsUnavailable);
        StringAssert.Contains(second.StatusMessage, "401");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class ScriptedHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
