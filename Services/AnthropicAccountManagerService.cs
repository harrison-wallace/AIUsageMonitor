using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIUsageMonitor.Collectors;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

// Creates and maintains managed Claude accounts: each one is a dedicated CLAUDE_CONFIG_DIR
// under %LOCALAPPDATA%\AIUsageMonitor\accounts\anthropic that the official claude CLI
// logs into once; afterwards the app reads and refreshes that dir's .credentials.json itself.
public sealed class AnthropicAccountManagerService
{
    // Temporary login folders. DiscardStaging deletes only folders with this prefix.
    private const string StagingPrefix = ".login-";

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly AppSettingsService _settingsService;
    private readonly AppLogService _logService;
    private readonly HttpClient _httpClient;
    private readonly ClaudeBillingService _billingService;

    public AnthropicAccountManagerService(
        AppSettingsService settingsService,
        AppLogService logService,
        HttpClient? httpClient = null,
        string? accountsRootDirectory = null,
        ClaudeBillingService? billingService = null)
    {
        _settingsService = settingsService;
        _logService = logService;
        _httpClient = httpClient ?? SharedHttpClient;
        _billingService = billingService ?? new ClaudeBillingService(settingsService);
        AccountsRootDirectory = accountsRootDirectory ?? DefaultAccountsRootDirectory;
    }

    public static string DefaultAccountsRootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIUsageMonitor",
        "accounts",
        "anthropic");

    public string AccountsRootDirectory { get; }

    public ProviderAccount CreateAccount(string label)
    {
        var slug = BuildSlug(label);
        var configDir = Path.Combine(AccountsRootDirectory, slug);
        Directory.CreateDirectory(configDir);

        var account = new ProviderAccount
        {
            Id = $"anthropic-{slug}",
            ProviderName = KnownProviders.Anthropic,
            Label = label.Trim(),
            Enabled = true,
            IsDefault = false,
            ConfigDir = configDir
        };

        _settingsService.Update(settings => settings.ProviderAccounts.Add(account));
        _logService.Info("Anthropic", $"Created managed Claude account '{account.Label}' at {configDir}.");
        return account;
    }

    public bool RemoveAccount(string accountId)
    {
        ProviderAccount? account = null;
        _settingsService.Update(settings =>
        {
            account = settings.ProviderAccounts.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, accountId, StringComparison.OrdinalIgnoreCase) && !candidate.IsDefault);
            if (account is not null) settings.ProviderAccounts.Remove(account);
        });
        if (account is null)
        {
            return false;
        }

        if (account.ClaudeBilling is { } connection)
        {
            try { _billingService.DeleteProfileAsync(connection.Id).GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _logService.Warning("Anthropic", "Removed the billing connection, but its browser profile could not be deleted."); }
        }

        // Only ever delete dirs we created under our own accounts root; never ~/.claude.
        if (!string.IsNullOrWhiteSpace(account.ConfigDir) &&
            Path.GetFullPath(account.ConfigDir).StartsWith(Path.GetFullPath(AccountsRootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(account.ConfigDir))
        {
            try
            {
                Directory.Delete(account.ConfigDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logService.Error("Anthropic", $"Could not delete account directory {account.ConfigDir}: {ex.Message}");
            }
        }

        _logService.Info("Anthropic", $"Removed managed Claude account '{account.Label}'.");
        return true;
    }

    public bool RenameAccount(string accountId, string newLabel)
    {
        if (string.IsNullOrWhiteSpace(newLabel))
        {
            return false;
        }

        var found = false;
        _settingsService.Update(settings =>
        {
            var account = settings.ProviderAccounts.FirstOrDefault(candidate => candidate.Id == accountId);
            if (account is null) return;
            account.Label = newLabel.Trim();
            found = true;
        });
        return found;
    }

    public bool SetAccountEnabled(string accountId, bool enabled)
    {
        var found = false;
        _settingsService.Update(settings =>
        {
            var account = settings.ProviderAccounts.FirstOrDefault(candidate => candidate.Id == accountId);
            if (account is null) return;
            account.Enabled = enabled;
            found = true;
        });
        return found;
    }

    // Starts `claude auth login` into a fresh staging folder. Nothing is saved until
    // CompleteLoginAsync confirms which account signed in, so a wrong-account or duplicate
    // login never replaces a working one.
    // `claudePath` is for tests; normally the CLI on PATH is used.
    public PendingLogin StartLogin(string? emailHint, string? claudePath = null)
    {
        claudePath ??= CommandPathResolver.FindCommandPath("claude")
            ?? throw new InvalidOperationException("The claude CLI was not found on PATH. Install Claude Code first.");
        var stagingDirectory = Path.Combine(AccountsRootDirectory, StagingPrefix + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var noBrowserCommand = ClaudeLoginSession.EnsureNoBrowserCommand(AccountsRootDirectory);
            return new PendingLogin(ClaudeLoginSession.Start(claudePath, stagingDirectory, emailHint, noBrowserCommand), stagingDirectory);
        }
        catch
        {
            DiscardStaging(stagingDirectory);
            throw;
        }
    }

    // Waits for the CLI to accept the pasted code, confirms the identity, then saves it: as a new
    // account when `existing` is null, otherwise into that account. On any failure the staging
    // folder is deleted and settings are unchanged.
    public async Task<LoginResult> CompleteLoginAsync(
        PendingLogin pending,
        ProviderAccount? existing,
        string label,
        string? emailHint,
        CancellationToken cancellationToken)
    {
        try
        {
            var exitCode = await pending.Session.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var credentialsPath = Path.Combine(pending.StagingDirectory, ".credentials.json");
            if (exitCode != 0 || !File.Exists(credentialsPath))
            {
                return LoginResult.Failed(DescribeFailedLogin(pending.Session.Output));
            }

            var identity = await TryFetchIdentityAsync(credentialsPath, cancellationToken).ConfigureAwait(false);
            if (identity is null || string.IsNullOrWhiteSpace(identity.Uuid))
            {
                return LoginResult.Failed("The sign-in finished, but the app could not confirm which account it was. Nothing was saved. Try again.");
            }

            if (ValidateLogin(_settingsService.Load(), existing?.Id, identity, emailHint) is { } rejection)
            {
                _logService.Warning("Anthropic", $"Rejected a Claude sign-in as {identity.Email}: {rejection}");
                return LoginResult.Failed(rejection);
            }

            var account = existing is null
                ? CommitNewAccount(pending.StagingDirectory, label, identity)
                : CommitRelogin(existing, pending.StagingDirectory, identity);
            if (account is null)
            {
                return LoginResult.Failed("The sign-in worked, but its login files could not be saved. Nothing was changed. Try again.");
            }

            await EnsureIdentityBlockAsync(account.ConfigDir, identity, cancellationToken).ConfigureAwait(false);
            return LoginResult.Successful(identity.Email, account, identity);
        }
        finally
        {
            pending.Session.Dispose();
            DiscardStaging(pending.StagingDirectory);
        }
    }

    public void CancelLogin(PendingLogin pending)
    {
        pending.Session.Dispose();
        DiscardStaging(pending.StagingDirectory);
    }

    // Null when the login can be saved; otherwise the reason it cannot, written for the user.
    internal static string? ValidateLogin(AppSettings settings, string? accountId, AccountIdentity identity, string? emailHint)
    {
        var expectedEmail = emailHint?.Trim();
        if (!string.IsNullOrWhiteSpace(expectedEmail) &&
            !string.IsNullOrWhiteSpace(identity.Email) &&
            !string.Equals(expectedEmail, identity.Email, StringComparison.OrdinalIgnoreCase))
        {
            return $"You signed in as {identity.Email}, not {expectedEmail}. Nothing was saved. " +
                $"Start again and choose {expectedEmail} on the sign-in page.";
        }

        var duplicate = settings.GetAccounts(KnownProviders.Anthropic).FirstOrDefault(account =>
            !account.IsDefault &&
            !string.Equals(account.Id, accountId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(account.AccountUuid) &&
            string.Equals(account.AccountUuid, identity.Uuid, StringComparison.OrdinalIgnoreCase));
        return duplicate is null
            ? null
            : $"You signed in as {identity.Email}, which is already saved as '{duplicate.Label}'. Nothing was saved. " +
                "Start again and choose a different account on the sign-in page.";
    }

    private ProviderAccount? CommitNewAccount(string stagingDirectory, string label, AccountIdentity identity)
    {
        var slug = BuildSlug(label);
        var configDir = Path.Combine(AccountsRootDirectory, slug);
        try
        {
            Directory.Move(stagingDirectory, configDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logService.Error("Anthropic", $"Could not save the new Claude login folder: {ex.Message}");
            return null;
        }

        var account = new ProviderAccount
        {
            Id = $"anthropic-{slug}",
            ProviderName = KnownProviders.Anthropic,
            Label = label.Trim(),
            Enabled = true,
            IsDefault = false,
            ConfigDir = configDir,
            Email = identity.Email,
            AccountUuid = identity.Uuid
        };
        _settingsService.Update(settings => settings.ProviderAccounts.Add(account));
        _logService.Info("Anthropic", $"Added Claude account '{account.Label}' signed in as {identity.Email}.");
        return account;
    }

    // Token and identity block always move together; see docs/claude-accounts.md.
    private ProviderAccount? CommitRelogin(ProviderAccount account, string stagingDirectory, AccountIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(account.ConfigDir))
        {
            return null;
        }

        if (!ClaudeIdentityFiles.TryCopyCredentials(
                Path.Combine(stagingDirectory, ".credentials.json"),
                Path.Combine(account.ConfigDir, ".credentials.json"),
                out var error))
        {
            _logService.Error("Anthropic", $"Could not save the new login for '{account.Label}': {error}");
            return null;
        }

        var stagedClaudeJson = ClaudeIdentityFiles.ClaudeJsonPathFor(stagingDirectory);
        if (string.Equals(ClaudeIdentityFiles.TryReadOAuthAccountUuid(stagedClaudeJson), identity.Uuid, StringComparison.OrdinalIgnoreCase))
        {
            ClaudeIdentityFiles.TryCopyOAuthAccountBlock(stagedClaudeJson, ClaudeIdentityFiles.ClaudeJsonPathFor(account.ConfigDir), out _);
        }

        PersistLoginIdentity(account, identity);
        return account;
    }

    // The CLI usually writes the oauthAccount block itself. When it does not, rebuild it from the
    // profile endpoint so `/status` in that folder names the account whose token it holds.
    private async Task EnsureIdentityBlockAsync(string configDir, AccountIdentity identity, CancellationToken cancellationToken)
    {
        var claudeJsonPath = ClaudeIdentityFiles.ClaudeJsonPathFor(configDir);
        if (string.Equals(ClaudeIdentityFiles.TryReadOAuthAccountUuid(claudeJsonPath), identity.Uuid, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var block = await TryFetchOAuthAccountBlockAsync(Path.Combine(configDir, ".credentials.json"), cancellationToken).ConfigureAwait(false);
        if (block is not null)
        {
            ClaudeIdentityFiles.TryWriteOAuthAccountBlock(claudeJsonPath, block, out _);
        }
    }

    // Deletes a staging folder, and only a staging folder under this service's accounts root.
    private void DiscardStaging(string stagingDirectory)
    {
        var root = Path.GetFullPath(AccountsRootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(stagingDirectory);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(full).StartsWith(StagingPrefix, StringComparison.Ordinal) ||
            !Directory.Exists(full))
        {
            return;
        }

        try
        {
            Directory.Delete(full, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logService.Warning("Anthropic", $"Could not delete the temporary login folder {full}: {ex.Message}");
        }
    }

    private static string DescribeFailedLogin(string output)
    {
        // The CLI's "Paste code here if prompted >" prompt has no line break, so an error printed
        // after the code arrives shares its line. Strip the prompt, and skip the link line.
        const string prompt = "Paste code here if prompted >";
        var lastLine = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.StartsWith(prompt, StringComparison.OrdinalIgnoreCase) ? line[prompt.Length..].Trim() : line)
            .LastOrDefault(line => line.Length > 0 && !line.Contains("/oauth/authorize", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(lastLine)
            ? "The sign-in did not finish. Nothing was saved. Check the code and try again."
            : $"The sign-in did not finish ({lastLine}). Nothing was saved. Check the code and try again.";
    }

    internal static string? TryReadAccessToken(string credentialsPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(credentialsPath));
            if (document.RootElement.TryGetProperty("claudeAiOauth", out var oauth) &&
                oauth.ValueKind == JsonValueKind.Object &&
                oauth.TryGetProperty("accessToken", out var tokenElement))
            {
                return tokenElement.GetString();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    public async Task<AccountIdentity?> TryFetchIdentityAsync(string credentialsPath, CancellationToken cancellationToken)
    {
        var accessToken = TryReadAccessToken(credentialsPath);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ParseIdentity(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    // Rebuilds the `oauthAccount` block Claude Code caches in .claude.json (the block
    // /status displays) from the live profile endpoint, keyed by whatever token the given
    // credentials file holds. Used to heal a stale or missing block during account switches.
    public async Task<JsonObject?> TryFetchOAuthAccountBlockAsync(string credentialsPath, CancellationToken cancellationToken)
    {
        var accessToken = TryReadAccessToken(credentialsPath);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var block = new JsonObject();

            if (root.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
            {
                AddStringProperty(block, "accountUuid", account, "uuid");
                AddStringProperty(block, "emailAddress", account, "email");
                AddStringProperty(block, "displayName", account, "display_name");
            }

            if (root.TryGetProperty("organization", out var organization) && organization.ValueKind == JsonValueKind.Object)
            {
                AddStringProperty(block, "organizationUuid", organization, "uuid");
                AddStringProperty(block, "organizationName", organization, "name");
                AddStringProperty(block, "organizationType", organization, "organization_type");
                AddStringProperty(block, "organizationRateLimitTier", organization, "rate_limit_tier");
                AddStringProperty(block, "billingType", organization, "billing_type");
                if (organization.TryGetProperty("has_extra_usage_enabled", out var extraUsage) &&
                    extraUsage.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    block["hasExtraUsageEnabled"] = extraUsage.GetBoolean();
                }
            }

            return block["accountUuid"] is null ? null : block;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static void AddStringProperty(JsonObject block, string blockKey, JsonElement source, string sourceKey)
    {
        if (source.TryGetProperty(sourceKey, out var element) && element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                block[blockKey] = value;
            }
        }
    }

    internal static AccountIdentity? ParseIdentity(JsonElement profileRoot)
    {
        if (profileRoot.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var email = string.Empty;
        var uuid = string.Empty;

        if (profileRoot.TryGetProperty("account", out var accountElement) &&
            accountElement.ValueKind == JsonValueKind.Object)
        {
            if (accountElement.TryGetProperty("email", out var emailElement))
            {
                email = emailElement.GetString() ?? string.Empty;
            }

            if (accountElement.TryGetProperty("uuid", out var uuidElement))
            {
                uuid = uuidElement.GetString() ?? string.Empty;
            }
            else if (accountElement.TryGetProperty("id", out var idElement))
            {
                uuid = idElement.GetString() ?? string.Empty;
            }
        }

        return string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(uuid)
            ? null
            : new AccountIdentity(email, uuid);
    }

    private void PersistLoginIdentity(ProviderAccount account, AccountIdentity? identity)
    {
        ProviderAccount? saved = null;
        _settingsService.Update(settings =>
        {
            saved = settings.ProviderAccounts.FirstOrDefault(candidate => candidate.Id == account.Id);
            if (saved is null || identity is null) return;
            saved.Email = identity.Email;
            saved.AccountUuid = identity.Uuid;
            if (saved.ClaudeBilling is { } billing && billing.AccountUuid != identity.Uuid)
            {
                billing.State = ClaudeBillingConnectionState.ReconnectRequired;
                billing.Message = "Claude account changed. Reconnect usage credits.";
            }
            if (string.IsNullOrWhiteSpace(saved.Label) && !string.IsNullOrWhiteSpace(identity.Email)) saved.Label = identity.Email;
        });
        if (saved is null) return;
        account.Email = saved.Email;
        account.AccountUuid = saved.AccountUuid;
        account.Label = saved.Label;
        _logService.Info(
            "Anthropic",
            $"Managed Claude account '{saved.Label}' logged in{(string.IsNullOrWhiteSpace(saved.Email) ? "" : $" as {saved.Email}")}.");
    }

    private static string BuildSlug(string label)
    {
        var cleaned = new string(label
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');
        if (cleaned.Length > 24)
        {
            cleaned = cleaned[..24].Trim('-');
        }

        if (cleaned.Length == 0)
        {
            cleaned = "account";
        }

        return $"{cleaned}-{Guid.NewGuid().ToString("N")[..4]}";
    }

    public sealed record AccountIdentity(string Email, string Uuid);

    public sealed record PendingLogin(ClaudeLoginSession Session, string StagingDirectory);

    public sealed record LoginResult(bool Succeeded, string Message, ProviderAccount? Account = null, AccountIdentity? Identity = null)
    {
        public static LoginResult Successful(string email, ProviderAccount account, AccountIdentity identity) => new(true, email, account, identity);

        public static LoginResult Failed(string message) => new(false, message);
    }
}
