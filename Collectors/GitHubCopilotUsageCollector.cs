using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Collectors;

// GitHub Copilot monthly quotas (premium requests, chat, completions) for the signed-in GitHub
// account. One account quota covers Copilot CLI, VS Code, and every other editor.
//
// Source: GET https://api.github.com/copilot_internal/user, the endpoint the Copilot editor
// integrations call. It is undocumented and may change. It is read-only and uses no quota.
// Token: COPILOT_GITHUB_TOKEN, GH_TOKEN, or GITHUB_TOKEN, else `gh auth token`, read on each
// refresh. The token is never stored.
public sealed class GitHubCopilotUsageCollector : IUsageCollector
{
    private const string Source = "GitHub Copilot user endpoint";
    private static readonly Uri UserEndpoint = new("https://api.github.com/copilot_internal/user");
    private static readonly TimeSpan GhTimeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    // Quota snapshot keys, in display order.
    private static readonly (string Key, string Title)[] Quotas =
    [
        ("premium_interactions", "Premium requests"),
        ("chat", "Chat"),
        ("completions", "Completions")
    ];

    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task<string?>> _tokenProvider;

    public GitHubCopilotUsageCollector(HttpClient? httpClient = null, Func<CancellationToken, Task<string?>>? tokenProvider = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _tokenProvider = tokenProvider ?? ReadTokenAsync;
    }

    public string ProviderName => KnownProviders.GitHubCopilot;

    public async Task<ProviderUsage> CollectAsync(CancellationToken cancellationToken)
    {
        // Read the token every time, so `gh auth switch` or a new GH_TOKEN takes effect on the next refresh.
        var token = await _tokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return ProviderUsageFactory.Unavailable(
                ProviderName,
                "No GitHub login found. Install the GitHub CLI and run gh auth login with your Copilot account.",
                Source);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("token", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AIUsageMonitor", "1.0"));

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                return ProviderUsageFactory.Unavailable(
                    ProviderName,
                    $"GitHub refused the Copilot request ({(int)response.StatusCode}). Check that gh is signed in to the account that has Copilot.",
                    Source);
            }

            if (!response.IsSuccessStatusCode)
            {
                return ProviderUsageFactory.Unavailable(ProviderName, $"GitHub returned {(int)response.StatusCode}.", Source);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return Parse(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return ProviderUsageFactory.Unavailable(ProviderName, $"Could not read Copilot usage: {ex.Message}", Source);
        }
    }

    internal static ProviderUsage Parse(JsonElement root)
    {
        var resetAt = ParseResetDate(root);
        var windows = new List<UsageWindow>();
        if (root.TryGetProperty("quota_snapshots", out var snapshots) && snapshots.ValueKind == JsonValueKind.Object)
        {
            foreach (var (key, title) in Quotas)
            {
                if (snapshots.TryGetProperty(key, out var snapshot) && snapshot.ValueKind == JsonValueKind.Object)
                {
                    windows.Add(ParseQuota(title, snapshot, resetAt));
                }
            }
        }

        var plan = FormatPlan(ProviderJson.TryGetString(root, "access_type_sku"), ProviderJson.TryGetString(root, "copilot_plan"));
        if (windows.Count == 0)
        {
            return ProviderUsageFactory.Unavailable(KnownProviders.GitHubCopilot, "This GitHub account has no Copilot quota data.", Source, plan);
        }

        return new ProviderUsage
        {
            Name = KnownProviders.GitHubCopilot,
            PlanName = plan,
            Source = Source,
            StatusMessage = $"GitHub Copilot {plan} monthly quotas, shared by Copilot CLI and the editors.",
            LastCheckedAt = DateTimeOffset.Now,
            Windows = windows
        };
    }

    private static UsageWindow ParseQuota(string title, JsonElement snapshot, DateTimeOffset? resetAt)
    {
        var unlimited = TryGetBool(snapshot, "unlimited") == true;
        ProviderJson.TryGetDouble(snapshot, "entitlement", out var entitlement);
        if (unlimited)
        {
            return ProviderUsageFactory.InactiveWindow(title, "Unlimited");
        }

        if (entitlement <= 0)
        {
            return ProviderUsageFactory.InactiveWindow(title);
        }

        if (!ProviderJson.TryGetDouble(snapshot, "remaining", out var remaining) &&
            !ProviderJson.TryGetDouble(snapshot, "quota_remaining", out remaining))
        {
            remaining = entitlement;
        }

        remaining = Math.Clamp(remaining, 0, entitlement);
        ProviderJson.TryGetDouble(snapshot, "overage_count", out var overage);
        var detail = $"{entitlement - remaining:0} of {entitlement:0} used this month";
        if (overage > 0)
        {
            detail += $", {overage:0} over";
        }

        return new UsageWindow
        {
            Title = title,
            Limit = entitlement,
            Used = entitlement - remaining,
            Remaining = remaining,
            ResetAt = resetAt,
            Detail = detail
        };
    }

    private static DateTimeOffset? ParseResetDate(JsonElement root)
    {
        foreach (var name in new[] { "quota_reset_date_utc", "quota_reset_date" })
        {
            if (ProviderJson.TryGetString(root, name) is { } text &&
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
            {
                return value;
            }
        }

        return null;
    }

    // access_type_sku is the plan; copilot_plan only says individual, business, or enterprise.
    internal static string FormatPlan(string? sku, string? copilotPlan)
    {
        var normalized = sku?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Contains("free"))
        {
            return "Free";
        }

        if (normalized.Contains("pro_plus") || normalized.Contains("proplus") || normalized.Contains("pro+"))
        {
            return "Pro+";
        }

        if (normalized.Contains("pro") || normalized.Contains("monthly_subscriber") || normalized.Contains("yearly_subscriber"))
        {
            return "Pro";
        }

        return copilotPlan?.Trim().ToLowerInvariant() switch
        {
            "business" => "Business",
            "enterprise" => "Enterprise",
            _ => PlanNameFormatter.Format(string.IsNullOrWhiteSpace(sku) ? copilotPlan : sku)
        };
    }

    private static bool? TryGetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static async Task<string?> ReadTokenAsync(CancellationToken cancellationToken)
    {
        foreach (var variable in new[] { "COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        var ghPath = CommandPathResolver.FindCommandPath("gh");
        if (string.IsNullOrWhiteSpace(ghPath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ghPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("auth");
        startInfo.ArgumentList.Add("token");
        startInfo.ArgumentList.Add("--hostname");
        startInfo.ArgumentList.Add("github.com");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(GhTimeout);
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return process.ExitCode == 0 ? output.Trim() : null;
            }
            catch (OperationCanceledException)
            {
                // A gh that hangs (for example on a keyring prompt) must not be left running.
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                throw;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
