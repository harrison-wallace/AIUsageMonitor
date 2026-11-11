using System.Windows.Media;
using AIUsageMonitor.Models;
using MediaBrush = System.Windows.Media.Brush;

namespace AIUsageMonitor.ViewModels;

// The full-size overlay is one table: a group per provider, a row per account, and the same
// columns for every account (docs/adr/0001-overlay-account-table.md).
public sealed record UsageTableCell(double UsedPercent, string PercentText, string SubText, MediaBrush Brush, string ToolTipText)
{
    public static readonly UsageTableCell Empty = new(0, string.Empty, string.Empty, UsageBrushes.Mute, string.Empty);

    public bool HasValue => PercentText.Length > 0;
}

public sealed record UsageTableRow(
    string SourceProviderName,
    string AccountText,
    string AccountDetailText,
    UsageTableCell Session,
    UsageTableCell Week,
    UsageTableCell Other,
    string CreditsText,
    string CreditsDetailText,
    string StatusLabel,
    MediaBrush StatusBrush,
    ImageSource StatusIcon,
    string ToolTipText,
    bool IsUnused,
    bool IsUnavailable)
{
    public bool HasCreditsDetail => CreditsDetailText.Length > 0;

    public bool HasAccountDetail => AccountDetailText.Length > 0;
}

public sealed record UsageTableGroup(string ProviderName, IReadOnlyList<UsageTableRow> Rows)
{
    public string Eyebrow => ProviderName.ToUpperInvariant();

    public ProviderLogos.Logo? ProviderLogo { get; } = ProviderLogos.For(ProviderName);

    public Geometry? Logo => ProviderLogo?.Geometry;

    public bool HasLogo => ProviderLogo is not null;

    public MediaBrush AccentBrush => UsageBrushes.ProviderAccent(ProviderName);

    // The logo replaces the provider name. A provider with no logo keeps the name.
    public string EyebrowText => HasLogo ? string.Empty : Eyebrow;
}

// HasCredits is false when no shown row has a balance or a money cap; the column then collapses.
public sealed record UsageTable(IReadOnlyList<UsageTableGroup> Groups, IReadOnlySet<string> HiddenSources, string HiddenText, bool HasCredits);

public static class UsageTableBuilder
{
    private const string Checking = "Checking";

    public static UsageTable Build(IEnumerable<ProviderUsageCard> cards, bool showUnused)
    {
        // A source provider can render as several cards (a model group such as "Fable" splits
        // into its own card). In the table they are one account again.
        var rows = cards
            .GroupBy(card => card.SourceProviderName, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildRow(group.ToList()))
            .ToList();

        // When every row is unused, hiding them all would leave an empty overlay.
        var hideUnused = !showUnused && rows.Any(row => !row.IsUnused);
        var hidden = hideUnused ? rows.Where(row => row.IsUnused).ToList() : [];
        var groups = rows
            .Where(row => !hidden.Contains(row))
            .GroupBy(row => ProviderGroupName(row.SourceProviderName))
            .Select(group => new UsageTableGroup(group.Key, group.ToList()))
            .ToList();
        var hiddenNames = hidden.Select(row => DisplayName(row)).Distinct().ToList();
        var hiddenText = hiddenNames.Count == 0
            ? string.Empty
            : $"{hiddenNames.Count} hidden, not used right now: {string.Join(", ", hiddenNames)}";
        return new UsageTable(
            groups,
            hidden.Select(row => row.SourceProviderName).ToHashSet(StringComparer.OrdinalIgnoreCase),
            hiddenText,
            groups.SelectMany(group => group.Rows).Any(row => row.CreditsText.Length > 0 || row.CreditsDetailText.Length > 0));
    }

    // "Anthropic - Work" -> "Anthropic"; "Anthropic API Credits" -> "Anthropic".
    internal static string ProviderGroupName(string sourceProviderName)
    {
        if (sourceProviderName.StartsWith(KnownProviders.AnthropicApiCredits, StringComparison.OrdinalIgnoreCase))
        {
            return KnownProviders.Anthropic;
        }

        var separator = sourceProviderName.IndexOf(" - ", StringComparison.Ordinal);
        return separator > 0 ? sourceProviderName[..separator] : sourceProviderName;
    }

    private static UsageTableRow BuildRow(IReadOnlyList<ProviderUsageCard> cards)
    {
        var first = cards[0];
        var source = first.SourceProviderName;
        var isChecking = cards.Any(card => card.IsChecking);
        var isUnavailable = cards.All(card => card.IsUnavailable);
        var windows = cards.SelectMany(card => card.Windows).Where(window => !window.IsInactive).ToList();
        var balances = windows.Where(window => window.IsBalance).ToList();
        var moneyCaps = windows.Where(window => !window.IsBalance && window.HasCustomText).ToList();
        var quotas = windows.Where(window => !window.IsBalance && !window.HasCustomText).ToList();

        var session = quotas.FirstOrDefault(window => IsSession(window.Title));
        var week = quotas.FirstOrDefault(window => IsWeek(window.Title));
        var others = quotas.Where(window => window != session && window != week).OrderByDescending(window => window.UsedPercent).ToList();

        var account = AccountText(cards, source);
        var plan = cards.Select(card => card.PlanName).FirstOrDefault(name => name.Length > 0) ?? string.Empty;
        var detail = isUnavailable && !isChecking
            ? first.StatusMessage
            : string.Equals(plan, account, StringComparison.OrdinalIgnoreCase) ? string.Empty : plan;

        var status = isUnavailable
            ? UsageStatus.Unavailable
            : quotas.Count > 0
            ? UsageStatus.FromUsedPercent(quotas.Max(window => window.UsedPercent))
            : UsageStatus.Balance;
        var isUnused = !isChecking &&
            (isUnavailable || (balances.Count == 0 && moneyCaps.Count == 0 && quotas.Count > 0 && quotas.All(window => window.UsedPercent <= 0)));

        return new UsageTableRow(
            source,
            account,
            detail,
            Cell(session, includeTitle: false),
            Cell(week, includeTitle: false),
            others.Count == 0 ? UsageTableCell.Empty : Cell(others[0], includeTitle: true, more: others.Count - 1),
            string.Join(" · ", balances.Select(window => TrimCreditsWord(window.ValueText))),
            string.Join(" · ", moneyCaps.Select(window => $"{window.Title} {window.ValueText}")),
            isChecking ? Checking : status.Label,
            isChecking ? UsageBrushes.Mute : status.Foreground,
            isChecking ? UsageStatus.Unavailable.Icon : status.Icon,
            StatusToolTip(isChecking ? Checking : status.Label, first.StatusMessage),
            isUnused,
            isUnavailable && !isChecking);
    }

    private static UsageTableCell Cell(UsageWindowDisplay? window, bool includeTitle, int more = 0)
    {
        if (window is null)
        {
            return UsageTableCell.Empty;
        }

        var parts = new List<string>();
        if (includeTitle)
        {
            parts.Add(more > 0 ? $"{window.Title} +{more}" : window.Title);
        }

        if (window.ResetShortText.Length > 0)
        {
            parts.Add(window.ResetShortText);
        }

        var resetDetail = window.ResetText.Length > 0 && window.ResetRelativeText.Length > 0
            ? $"{window.ResetText} ({window.ResetRelativeText})"
            : window.ResetText;
        return new UsageTableCell(
            window.UsedPercent,
            $"{window.UsedPercent:0}%",
            string.Join(" · ", parts),
            UsageStatus.FromUsedPercent(window.UsedPercent).Foreground,
            resetDetail.Length > 0 ? $"{window.Title}: {window.UsedPercent:0}% used. {resetDetail}." : $"{window.Title}: {window.UsedPercent:0}% used.");
    }

    // The column header already says Credits: "$113.85 credits" -> "$113.85".
    internal static string TrimCreditsWord(string text) =>
        text.EndsWith(" credits", StringComparison.OrdinalIgnoreCase) ? text[..^8] : text;

    private static string StatusToolTip(string label, string message) =>
        string.IsNullOrWhiteSpace(message) ? label : $"{label}. {message}";

    private static string AccountText(IReadOnlyList<ProviderUsageCard> cards, string source)
    {
        var label = cards.Select(card => card.AccountLabel).FirstOrDefault(text => text.Length > 0);
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        if (source.StartsWith(KnownProviders.AnthropicApiCredits, StringComparison.OrdinalIgnoreCase))
        {
            return "API credits";
        }

        var separator = source.IndexOf(" - ", StringComparison.Ordinal);
        if (separator > 0)
        {
            return source[(separator + 3)..];
        }

        return cards.Select(card => card.PlanName).FirstOrDefault(name => name.Length > 0) ?? source;
    }

    private static string DisplayName(UsageTableRow row)
    {
        var group = ProviderGroupName(row.SourceProviderName);
        return string.Equals(group, row.SourceProviderName, StringComparison.OrdinalIgnoreCase)
            ? group
            : $"{group} {row.AccountText}";
    }

    private static bool IsSession(string title) =>
        title.Equals("5h", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("session", StringComparison.OrdinalIgnoreCase);

    private static bool IsWeek(string title) =>
        title.Equals("7d", StringComparison.OrdinalIgnoreCase) ||
        title.Equals("week", StringComparison.OrdinalIgnoreCase) ||
        title.Equals("weekly", StringComparison.OrdinalIgnoreCase);
}
