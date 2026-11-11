using AIUsageMonitor.Models;
using AIUsageMonitor.ViewModels;

namespace AIUsageMonitor.Tests;

// The full-size overlay table (docs/adr/0001-overlay-account-table.md).
[TestClass]
public sealed class UsageTableTests
{
    [TestMethod]
    public void ModelGroupCardRejoinsItsAccountRow()
    {
        var viewModel = new UsageOverlayViewModel();
        viewModel.ApplyProvider(Usage("Anthropic - Seth", "Seth", "Max 20x",
            Window("5h", 1), Window("7d", 12),
            new UsageWindow { Title = "Fable", DisplayGroupName = "Fable", Limit = 100, Used = 65, Remaining = 35 },
            new UsageWindow { Title = "Extra usage", DisplayGroupName = "Fable", Limit = 50, Used = 10, Remaining = 40, RemainingText = "$10 of $50" },
            new UsageWindow { Title = "Usage credits", Limit = 0, Remaining = 113.85, RemainingText = "$113.85 credits", IsBalance = true }));

        Assert.HasCount(2, viewModel.Providers, "The cards still split the Fable group.");
        var group = viewModel.TableGroups.Single();
        Assert.AreEqual("Anthropic", group.ProviderName);
        var row = group.Rows.Single();
        Assert.AreEqual("Seth", row.AccountText);
        Assert.AreEqual("Max 20x", row.AccountDetailText);
        Assert.AreEqual("1%", row.Session.PercentText);
        Assert.AreEqual("12%", row.Week.PercentText);
        Assert.AreEqual("65%", row.Other.PercentText);
        StringAssert.StartsWith(row.Other.SubText, "Fable");
        Assert.AreEqual("$113.85", row.CreditsText, "The column header already says Credits.");
        Assert.IsTrue(viewModel.HasCreditsColumn);
        Assert.AreEqual("Extra usage $10 of $50", row.CreditsDetailText);
        Assert.AreEqual("Good", row.StatusLabel, "65% used is Good on the VortexOne scale.");
    }

    [TestMethod]
    public void AccountsGroupUnderTheirProvider()
    {
        var viewModel = new UsageOverlayViewModel();
        viewModel.ApplyProvider(Usage("Anthropic - Work", "Work", "Max 5x", Window("5h", 43)));
        viewModel.ApplyProvider(Usage(KnownProviders.AnthropicApiCredits, "", "",
            new UsageWindow { Title = "Prepaid credits", Limit = 100, Remaining = 100, RemainingText = "$170.00 credits", IsBalance = true }));
        viewModel.ApplyProvider(Usage("OpenAI - Seth", "Seth", "Pro", Window("5h", 26)));

        CollectionAssert.AreEqual(new[] { "Anthropic", "OpenAI" }, viewModel.TableGroups.Select(group => group.ProviderName).ToArray());
        var api = viewModel.TableGroups[0].Rows[1];
        Assert.AreEqual("API credits", api.AccountText);
        Assert.AreEqual("Balance", api.StatusLabel);
        Assert.IsFalse(api.IsUnused, "A balance is in use even with no quota windows.");
    }

    [TestMethod]
    public void ActiveHidesIdleProvidersEverywhereAndAllShowsThem()
    {
        var viewModel = new UsageOverlayViewModel();
        viewModel.ApplyProvider(Usage("OpenAI - Work", "Work", "Plus", Window("5h", 65)));
        viewModel.ApplyProvider(Usage("Gemini", "", "Google One AI Pro", Window("Gemini Flash", 0), Window("Gemini Pro", 0)));
        viewModel.ApplyProvider(new ProviderUsage { Name = "Cursor", IsUnavailable = true, StatusMessage = "Cursor is not signed in." });

        Assert.IsFalse(viewModel.ShowUnusedProviders);
        Assert.AreEqual("OpenAI", viewModel.TableGroups.Single().ProviderName);
        Assert.AreEqual("OpenAI - Work", viewModel.VisibleProviders.Single().SourceProviderName, "The compact and mini strips hide them too.");
        Assert.AreEqual("2 hidden, not used right now: Gemini, Cursor", viewModel.HiddenProvidersText);

        viewModel.ShowUnusedProviders = true;

        Assert.HasCount(3, viewModel.TableGroups);
        Assert.HasCount(3, viewModel.VisibleProviders);
        Assert.IsFalse(viewModel.HasHiddenProviders);
        var cursor = viewModel.TableGroups.Single(group => group.ProviderName == "Cursor").Rows.Single();
        Assert.AreEqual("No data", cursor.StatusLabel);
        Assert.AreEqual("Cursor is not signed in.", cursor.AccountDetailText);
    }

    [TestMethod]
    public void EverythingShowsWhenEveryProviderIsUnused()
    {
        var viewModel = new UsageOverlayViewModel();
        viewModel.ApplyProvider(Usage("Gemini", "", "Google One AI Pro", Window("Gemini Flash", 0)));

        Assert.HasCount(1, viewModel.TableGroups);
        Assert.HasCount(1, viewModel.VisibleProviders);
        Assert.IsFalse(viewModel.HasHiddenProviders);
    }

    [TestMethod]
    public void CheckingProvidersAreNeverHidden()
    {
        var viewModel = new UsageOverlayViewModel();
        viewModel.ApplyProvider(Usage("OpenAI - Work", "Work", "Plus", Window("5h", 65)));
        viewModel.SetChecking(["OpenAI - Work", "Cursor"]);

        Assert.HasCount(2, viewModel.TableGroups);
        Assert.IsTrue(viewModel.TableGroups.SelectMany(group => group.Rows).All(row => row.StatusLabel == "Checking"));
    }

    [TestMethod]
    public void CreditsColumnCollapsesWhenNoShownAccountHasCredits()
    {
        var viewModel = new UsageOverlayViewModel();
        viewModel.ApplyProvider(Usage("OpenAI - Work", "Work", "Plus", Window("5h", 65), Window("7d", 20)));

        Assert.IsFalse(viewModel.HasCreditsColumn);
        Assert.AreEqual(string.Empty, viewModel.TableGroups.Single().Rows.Single().CreditsText);
    }

    [TestMethod]
    [DataRow(30, "30m")]
    [DataRow(240, "4h")]
    [DataRow(60 * 30, "30h")]
    [DataRow(60 * 24 * 3, "3d")]
    [DataRow(0.5, "now")]
    [DataRow(-5, "passed")]
    public void ResetTimesAreShort(double minutesAhead, string expected)
    {
        var now = DateTimeOffset.Now;
        Assert.AreEqual(expected, UsageWindowDisplay.FormatResetShortText(now.AddMinutes(minutesAhead), now));
    }

    [TestMethod]
    public void CellsCarryTheFullResetInTheirToolTip()
    {
        var viewModel = new UsageOverlayViewModel();
        viewModel.ApplyProvider(Usage("OpenAI - Work", "Work", "Plus", Window("5h", 65)));

        var session = viewModel.TableGroups.Single().Rows.Single().Session;
        Assert.AreEqual("3h", session.SubText);
        StringAssert.StartsWith(session.ToolTipText, "5h: 65% used. Resets ");
    }

    [TestMethod]
    [DataRow(0, "Optimal")]
    [DataRow(49, "Optimal")]
    [DataRow(50, "Good")]
    [DataRow(75, "Watch")]
    [DataRow(90, "Needs attention")]
    [DataRow(100, "Needs attention")]
    public void StatusScaleFollowsPercentUsed(double used, string label)
    {
        Assert.AreEqual(label, UsageStatus.FromUsedPercent(used).Label);
    }

    private static ProviderUsage Usage(string name, string accountLabel, string plan, params UsageWindow[] windows) => new()
    {
        Name = name,
        AccountLabel = accountLabel,
        PlanName = plan,
        Source = "Test",
        StatusMessage = "Test usage.",
        LastCheckedAt = DateTimeOffset.Now,
        Windows = windows.ToList()
    };

    private static UsageWindow Window(string title, double used) => new()
    {
        Title = title,
        Limit = 100,
        Used = used,
        Remaining = 100 - used,
        ResetAt = DateTimeOffset.Now.AddHours(3)
    };
}
