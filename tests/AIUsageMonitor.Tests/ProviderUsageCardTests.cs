using AIUsageMonitor.Models;
using AIUsageMonitor.ViewModels;

namespace AIUsageMonitor.Tests;

[TestClass]
public sealed class ProviderUsageCardTests
{
    [TestMethod]
    public void InactiveModelDoesNotDragCardToExhausted()
    {
        // Reproduces the free-Google-account case: usable "Flash" is full, but an unusable "Pro"
        // bucket comes back empty/inactive. The card headline + badge must come from Flash only.
        var usage = new ProviderUsage
        {
            Name = "Gemini",
            Source = "Test",
            StatusMessage = "Test usage.",
            LastCheckedAt = DateTimeOffset.Now,
            Windows =
            [
                new UsageWindow
                {
                    Title = "Gemini Flash",
                    Limit = 100,
                    Used = 0,
                    Remaining = 100,
                    ResetAt = DateTimeOffset.Now.AddHours(24)
                },
                new UsageWindow
                {
                    Title = "Gemini Pro",
                    Limit = 100,
                    Used = 100,
                    Remaining = 0,
                    ResetAt = null,
                    Detail = "Not on this plan",
                    IsInactive = true
                }
            ]
        };

        var card = new ProviderUsageCard(usage);

        Assert.AreEqual(100d, card.PrimaryRemainingPercent);
        Assert.AreEqual("Optimal", card.OverallStatusLabel);
        Assert.AreEqual(0d, card.PrimaryUsedPercent);
        StringAssert.EndsWith(card.SummaryText, " - 0% used");

        // The inactive row is still shown, just muted and excluded from the card status.
        Assert.AreEqual(2, card.Windows.Count);
        var pro = card.Windows.Single(window => window.Title == "Gemini Pro");
        Assert.IsTrue(pro.IsInactive);
        Assert.AreEqual("N/A", pro.ValueText);
        Assert.AreEqual("Not on this plan", pro.LimitText);
        Assert.AreEqual(string.Empty, pro.ResetRelativeText);
    }

    [TestMethod]
    public void CardWithOnlyInactiveModelsReportsNoData()
    {
        var usage = new ProviderUsage
        {
            Name = "Gemini",
            Source = "Test",
            StatusMessage = "Test usage.",
            LastCheckedAt = DateTimeOffset.Now,
            Windows =
            [
                new UsageWindow
                {
                    Title = "Gemini Pro",
                    Limit = 100,
                    Used = 100,
                    Remaining = 0,
                    IsInactive = true
                }
            ]
        };

        var card = new ProviderUsageCard(usage);

        Assert.AreEqual("No data", card.OverallStatusLabel);
        Assert.AreEqual(0d, card.PrimaryRemainingPercent);
    }

    [TestMethod]
    public void BalanceWindowUsesAmountSummaryWithoutProgress()
    {
        var usage = new ProviderUsage
        {
            Name = KnownProviders.AnthropicApiCredits,
            Source = "Test",
            StatusMessage = "Test usage.",
            LastCheckedAt = DateTimeOffset.Now,
            Windows =
            [
                new UsageWindow
                {
                    Title = "Prepaid credits",
                    Limit = 100,
                    Used = 0,
                    Remaining = 100,
                    RemainingText = "$170.00 credits",
                    Detail = "Verified now",
                    HideReset = true,
                    IsBalance = true
                }
            ]
        };

        var card = new ProviderUsageCard(usage);

        Assert.AreEqual("Anthropic API Credits - $170.00 credits", card.SummaryText);
        Assert.AreEqual("Anthropic API Credits - $170.00 credits", card.CompactSummaryText);
        Assert.AreEqual("Anthropic API", card.MiniSummaryText);
        Assert.AreEqual("$170.00 credits", card.BalanceSummaryText);
        Assert.AreEqual("$170.00", card.SecondaryBalanceText);
        Assert.IsTrue(card.IsBalanceOnly);
        Assert.IsTrue(card.HasSecondaryBalance);
        Assert.IsFalse(card.ShowsSummaryProgress);
        Assert.AreEqual(1, card.Windows.Count);
        Assert.IsTrue(card.Windows[0].IsBalance);
        Assert.IsTrue(card.Windows[0].IsHighlightedBalance);
        Assert.IsFalse(card.Windows[0].ShowsProgress);
    }

    [TestMethod]
    public void CreditsBalanceRowDoesNotChangeAQuotaCardHeadline()
    {
        var usage = new ProviderUsage
        {
            Name = KnownProviders.OpenAI,
            PlanName = "Pro",
            Source = "Test",
            StatusMessage = "Test usage.",
            LastCheckedAt = DateTimeOffset.Now,
            Windows =
            [
                new UsageWindow { Title = "7d", Limit = 100, Used = 100, Remaining = 0, ResetAt = DateTimeOffset.Now.AddDays(3) },
                new UsageWindow
                {
                    Title = "Credits",
                    Limit = 100,
                    Used = 0,
                    Remaining = 100,
                    RemainingText = "2,389 credits",
                    HideReset = true,
                    IsBalance = true
                }
            ]
        };

        var card = new ProviderUsageCard(usage);

        Assert.AreEqual("OpenAI - 100% used", card.SummaryText);
        Assert.AreEqual(0, card.PrimaryRemainingPercent);
        Assert.AreEqual(100, card.PrimaryUsedPercent);
        Assert.IsFalse(card.IsBalanceOnly);
        Assert.IsTrue(card.ShowsSummaryProgress);
        Assert.AreEqual(2, card.Windows.Count);
        Assert.IsTrue(card.Windows[1].IsBalance);
        Assert.IsFalse(card.Windows[1].ShowsProgress);
        Assert.AreEqual("2,389 credits", card.Windows[1].ValueText);
        Assert.AreEqual(string.Empty, card.Windows[1].ResetText);
        Assert.IsTrue(card.HasSecondaryBalance);
        Assert.AreEqual("2,389 credits", card.SecondaryBalanceText);
        Assert.IsTrue(card.Windows[1].IsHighlightedBalance);
        Assert.IsFalse(card.Windows[0].IsHighlightedBalance);
    }

    [TestMethod]
    public void SupplementalDisplayGroupsRenderAsSeparateProviderCards()
    {
        var viewModel = new UsageOverlayViewModel();
        var usage = new ProviderUsage
        {
            Name = "Anthropic",
            PlanName = "Max 20x",
            Source = "Test",
            StatusMessage = "Test usage.",
            LastCheckedAt = DateTimeOffset.Now,
            Windows =
            [
                new UsageWindow
                {
                    Title = "5h",
                    Limit = 100,
                    Used = 18,
                    Remaining = 82,
                    ResetAt = DateTimeOffset.Now.AddHours(3)
                },
                new UsageWindow
                {
                    Title = "Fable",
                    DisplayGroupName = "Fable",
                    Limit = 100,
                    Used = 100,
                    Remaining = 0,
                    ResetAt = DateTimeOffset.Now.AddHours(14)
                },
                new UsageWindow
                {
                    Title = "Extra usage",
                    DisplayGroupName = "Fable",
                    Limit = 50,
                    Used = 10,
                    Remaining = 40,
                    RemainingText = "$10 of $50",
                    Detail = "Monthly spend limit",
                    HideReset = true
                }
            ]
        };

        viewModel.ApplyProvider(usage);

        Assert.AreEqual(2, viewModel.Providers.Count);
        Assert.AreEqual("Anthropic (Max 20x)", viewModel.Providers[0].Name);
        Assert.AreEqual("Anthropic Fable", viewModel.Providers[1].Name);
        Assert.AreEqual("Fable", viewModel.Providers[1].ConstrainedShortName);
        Assert.AreEqual("Fable - 100%", viewModel.Providers[1].MiniSummaryText);
        Assert.IsTrue(viewModel.Providers.All(provider => provider.SourceProviderName == "Anthropic"));
        Assert.AreEqual("Needs attention", viewModel.Providers[1].OverallStatusLabel);
        Assert.AreEqual(2, viewModel.Providers[1].Windows.Count);
        Assert.AreEqual("$10 of $50", viewModel.Providers[1].Windows.Single(window => window.Title == "Extra usage").ValueText);

        viewModel.SetChecking(["Anthropic"]);

        Assert.AreEqual(2, viewModel.Providers.Count);
        Assert.IsTrue(viewModel.Providers.All(provider => provider.SummaryText.Contains("checking", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual("Fable - checking", viewModel.Providers[1].MiniSummaryText);
    }
    [TestMethod]
    public void QuotaRowsShowPercentUsedDerivedFromRemaining()
    {
        // Used is derived from Remaining, so the text, the bar, and the status colour agree even
        // when a provider reports a stale or missing Used value.
        var consistent = new UsageWindowDisplay(new UsageWindow { Title = "5h", Limit = 100, Used = 18, Remaining = 82 });
        var usedMissing = new UsageWindowDisplay(new UsageWindow { Title = "7d", Limit = 100, Used = 0, Remaining = 40 });
        var balance = new UsageWindowDisplay(new UsageWindow { Title = "Credits", Limit = 100, Remaining = 100, RemainingText = "2,389 credits", IsBalance = true });

        Assert.AreEqual("18% used", consistent.ValueText);
        Assert.AreEqual(18d, consistent.UsedPercent);
        Assert.AreEqual("60% used", usedMissing.ValueText);
        Assert.AreEqual(60d, usedMissing.UsedPercent);
        Assert.AreEqual("2,389 credits", balance.ValueText);
        Assert.IsFalse(balance.ShowsProgress);
    }
}
