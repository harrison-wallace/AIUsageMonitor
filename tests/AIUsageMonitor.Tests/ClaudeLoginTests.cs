using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Tests;

// The in-app Claude sign-in: no terminal, no browser opened by the CLI, and nothing saved until
// the app has checked which account signed in. A fake `claude` script stands in for the CLI.
[TestClass]
public sealed class ClaudeLoginTests
{
    private const string SampleOutput =
        "If the browser didn't open, visit: https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";

    private string _tempRoot = string.Empty;
    private string _accountsRoot = string.Empty;
    private string _fakeClaude = string.Empty;
    private AppSettingsService _settingsService = null!;
    private AppLogService _logService = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "AIUsageMonitor.Tests", Guid.NewGuid().ToString("N"));
        _accountsRoot = Path.Combine(_tempRoot, "accounts", "anthropic");
        Directory.CreateDirectory(_accountsRoot);
        _settingsService = new AppSettingsService(_tempRoot);
        _logService = new AppLogService(_tempRoot);
        _fakeClaude = Path.Combine(_tempRoot, "fake-claude.cmd");
        File.WriteAllText(_fakeClaude, string.Join("\r\n",
            "@echo off",
            "echo Opening browser to sign in...",
            "echo If the browser didn't open, visit: https://claude.com/cai/oauth/authorize?code=true^&state=x",
            "set /p CODE=Paste code here if prompted ^> ",
            "if \"%CODE%\"==\"bad\" (echo OAuth error: invalid code & exit /b 1)",
            "> \"%CLAUDE_CONFIG_DIR%\\.credentials.json\" echo {\"claudeAiOauth\":{\"accessToken\":\"tok-%CODE%\",\"refreshToken\":\"r\",\"expiresAt\":9999999999999}}",
            "exit /b 0",
            ""), Encoding.ASCII);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch (IOException) { }
    }

    [TestMethod]
    public void ExtractsTheSignInLinkFromCliOutput()
    {
        Assert.AreEqual(
            "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz",
            ClaudeLoginSession.ExtractAuthorizeUrl(SampleOutput));
        Assert.IsNull(ClaudeLoginSession.ExtractAuthorizeUrl("Opening browser to sign in…"));
    }

    [TestMethod]
    public void StartInfoUsesTheStagingFolderAndNeverOpensABrowser()
    {
        var info = ClaudeLoginSession.BuildStartInfo(@"C:\bin\claude.exe", @"C:\staging", " crypto@example.com ", @"C:\no-browser.cmd");

        CollectionAssert.AreEqual(new[] { "auth", "login", "--email", "crypto@example.com" }, info.ArgumentList.ToArray());
        Assert.AreEqual(@"C:\staging", info.Environment["CLAUDE_CONFIG_DIR"]);
        Assert.AreEqual(@"C:\no-browser.cmd", info.Environment["BROWSER"]);
        Assert.IsFalse(info.UseShellExecute);
        Assert.IsTrue(info.CreateNoWindow);

        var withoutEmail = ClaudeLoginSession.BuildStartInfo(@"C:\bin\claude.exe", @"C:\staging", null, @"C:\no-browser.cmd");
        CollectionAssert.AreEqual(new[] { "auth", "login" }, withoutEmail.ArgumentList.ToArray());
    }

    [TestMethod]
    public void ValidateLoginRejectsWrongEmailAndDuplicates()
    {
        var settings = new AppSettings();
        settings.ProviderAccounts.Add(new ProviderAccount
        {
            Id = "anthropic-main", ProviderName = KnownProviders.Anthropic, Label = "Main",
            AccountUuid = "uuid-main", Email = "main@example.com", ConfigDir = "x"
        });
        var main = new AnthropicAccountManagerService.AccountIdentity("main@example.com", "uuid-main");
        var crypto = new AnthropicAccountManagerService.AccountIdentity("crypto@example.com", "uuid-crypto");

        StringAssert.Contains(AnthropicAccountManagerService.ValidateLogin(settings, null, main, "crypto@example.com"), "not crypto@example.com");
        StringAssert.Contains(AnthropicAccountManagerService.ValidateLogin(settings, null, main, null), "already saved as 'Main'");
        Assert.IsNull(AnthropicAccountManagerService.ValidateLogin(settings, null, crypto, "CRYPTO@example.com"));
        Assert.IsNull(AnthropicAccountManagerService.ValidateLogin(settings, "anthropic-main", main, "main@example.com"),
            "Signing an account in again as itself is not a duplicate.");
    }

    [TestMethod]
    public async Task NewAccountIsSavedOnlyAfterTheIdentityIsChecked()
    {
        var manager = CreateManager("uuid-crypto", "crypto@example.com");

        var pending = manager.StartLogin("crypto@example.com", _fakeClaude);
        StringAssert.StartsWith(await pending.Session.AuthorizeUrl.WaitAsync(TimeSpan.FromSeconds(20)), "https://claude.com/cai/oauth/authorize?");
        Assert.HasCount(0, SavedAccounts(), "Nothing is saved while the sign-in is pending.");

        await pending.Session.SubmitCodeAsync("good");
        var result = await manager.CompleteLoginAsync(pending, null, "Crypto", "crypto@example.com", TimeoutToken());

        Assert.IsTrue(result.Succeeded, result.Message);
        var saved = SavedAccounts().Single();
        Assert.AreEqual("Crypto", saved.Label);
        Assert.AreEqual("uuid-crypto", saved.AccountUuid);
        Assert.AreEqual("crypto@example.com", saved.Email);
        StringAssert.Contains(File.ReadAllText(Path.Combine(saved.ConfigDir, ".credentials.json")), "tok-good");
        AssertNoStagingFolders();
    }

    [TestMethod]
    public async Task DuplicateLoginIsRejectedAndNothingIsSaved()
    {
        _settingsService.Update(settings => settings.ProviderAccounts.Add(new ProviderAccount
        {
            Id = "anthropic-main", ProviderName = KnownProviders.Anthropic, Label = "Harrison",
            AccountUuid = "uuid-main", Email = "main@example.com", ConfigDir = Path.Combine(_accountsRoot, "main")
        }));
        var manager = CreateManager("uuid-main", "main@example.com");

        var pending = manager.StartLogin(null, _fakeClaude);
        await pending.Session.AuthorizeUrl.WaitAsync(TimeSpan.FromSeconds(20));
        await pending.Session.SubmitCodeAsync("good");
        var result = await manager.CompleteLoginAsync(pending, null, "Crypto", null, TimeoutToken());

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "already saved as 'Harrison'");
        Assert.HasCount(1, SavedAccounts());
        AssertNoStagingFolders();
    }

    [TestMethod]
    public async Task FailedCodeSavesNothing()
    {
        var manager = CreateManager("uuid-crypto", "crypto@example.com");

        var pending = manager.StartLogin(null, _fakeClaude);
        await pending.Session.AuthorizeUrl.WaitAsync(TimeSpan.FromSeconds(20));
        await pending.Session.SubmitCodeAsync("bad");
        var result = await manager.CompleteLoginAsync(pending, null, "Crypto", null, TimeoutToken());

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "invalid code");
        Assert.HasCount(0, SavedAccounts());
        AssertNoStagingFolders();
    }

    [TestMethod]
    public async Task WrongAccountOnSignInAgainKeepsTheWorkingLogin()
    {
        var configDir = Path.Combine(_accountsRoot, "crypto");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, ".credentials.json"), "{\"claudeAiOauth\":{\"accessToken\":\"tok-original\"}}");
        var existing = new ProviderAccount
        {
            Id = "anthropic-crypto", ProviderName = KnownProviders.Anthropic, Label = "Crypto",
            AccountUuid = "uuid-crypto", Email = "crypto@example.com", ConfigDir = configDir
        };
        _settingsService.Update(settings => settings.ProviderAccounts.Add(existing));
        var manager = CreateManager("uuid-main", "main@example.com");

        var pending = manager.StartLogin("crypto@example.com", _fakeClaude);
        await pending.Session.AuthorizeUrl.WaitAsync(TimeSpan.FromSeconds(20));
        await pending.Session.SubmitCodeAsync("good");
        var result = await manager.CompleteLoginAsync(pending, existing, "Crypto", "crypto@example.com", TimeoutToken());

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "not crypto@example.com");
        StringAssert.Contains(File.ReadAllText(Path.Combine(configDir, ".credentials.json")), "tok-original");
        Assert.AreEqual("uuid-crypto", SavedAccounts().Single().AccountUuid);
        AssertNoStagingFolders();
    }

    [TestMethod]
    public void CancelDeletesTheStagingFolder()
    {
        var manager = CreateManager("uuid-crypto", "crypto@example.com");
        var pending = manager.StartLogin(null, _fakeClaude);
        Assert.IsTrue(Directory.Exists(pending.StagingDirectory));

        manager.CancelLogin(pending);

        AssertNoStagingFolders();
        Assert.HasCount(0, SavedAccounts());
    }

    [TestMethod]
    public void CurrentCliLoginIsAdoptedOnceSoItsCardStays()
    {
        var home = Path.Combine(_tempRoot, "home");
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        File.WriteAllText(Path.Combine(home, ".claude", ".credentials.json"), "{\"claudeAiOauth\":{\"accessToken\":\"tok-main\"}}");
        var manager = CreateManager("uuid-main", "main@example.com");
        var switchService = new ClaudeAccountSwitchService(_settingsService, _logService, manager, homeDirectory: home);
        var identity = new AnthropicAccountManagerService.AccountIdentity("main@example.com", "uuid-main");

        Assert.IsTrue(switchService.AdoptSlotLoginIfUnsaved(identity));
        Assert.IsFalse(switchService.AdoptSlotLoginIfUnsaved(identity), "An identity already saved is not adopted twice.");

        var adopted = SavedAccounts().Single();
        Assert.AreEqual("uuid-main", adopted.AccountUuid);
        StringAssert.Contains(File.ReadAllText(Path.Combine(adopted.ConfigDir, ".credentials.json")), "tok-main");
    }

    private AnthropicAccountManagerService CreateManager(string profileUuid, string profileEmail)
    {
        var profileJson = $"{{\"account\":{{\"uuid\":\"{profileUuid}\",\"email\":\"{profileEmail}\"}},\"organization\":{{\"uuid\":\"org-1\",\"name\":\"Org\"}}}}";
        var httpClient = new HttpClient(new ScriptedHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(profileJson, Encoding.UTF8, "application/json")
        }));
        return new AnthropicAccountManagerService(_settingsService, _logService, httpClient, _accountsRoot);
    }

    private List<ProviderAccount> SavedAccounts() =>
        _settingsService.Load().GetAccounts(KnownProviders.Anthropic).Where(account => !account.IsDefault).ToList();

    private void AssertNoStagingFolders() =>
        Assert.HasCount(0, Directory.GetDirectories(_accountsRoot, ".login-*"));

    private static CancellationToken TimeoutToken() => new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private sealed class ScriptedHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
