using AIUsageMonitor.Models;
using MediaBrush = System.Windows.Media.Brush;

namespace AIUsageMonitor.ViewModels;

public sealed class UsageWindowDisplay
{
    public UsageWindowDisplay(UsageWindow usageWindow, bool highlightBalance = false)
    {
        // Purchased balances, including standalone API credits, read consistently in amber.
        IsHighlightedBalance = highlightBalance && usageWindow.IsBalance && !usageWindow.IsInactive;
        Title = string.IsNullOrWhiteSpace(usageWindow.Title) ? "Usage" : usageWindow.Title;
        Limit = usageWindow.Limit;
        Used = usageWindow.Used;
        Remaining = usageWindow.EffectiveRemaining;
        // Derived from the remaining percent so the text, the bar, and the status colour agree.
        UsedPercent = 100 - usageWindow.RemainingPercent;
        RemainingPercent = usageWindow.RemainingPercent;
        Detail = usageWindow.Detail;
        IsBalance = usageWindow.IsBalance;
        IsInactive = usageWindow.IsInactive;
        ShowsProgress = !usageWindow.IsBalance;
        HasCustomText = !string.IsNullOrWhiteSpace(usageWindow.RemainingText);

        if (usageWindow.IsInactive)
        {
            // No usable quota on this plan: show a calm greyed row instead of an alarming
            // "100% used / reset passed". The card excludes inactive windows from its status.
            var muted = UsageStatus.Unavailable;
            ValueText = "N/A";
            LimitText = string.IsNullOrWhiteSpace(usageWindow.Detail) ? "Not on this plan" : usageWindow.Detail;
            ResetText = string.Empty;
            ResetRelativeText = string.Empty;
            ResetShortText = string.Empty;
            ResetRelativeBrush = UsageBrushes.FrozenBrush("#AEA69C");
            StatusLabel = muted.Label;
            StatusBrush = muted.Foreground;
            StatusBackground = muted.Background;
            StatusIcon = muted.Icon;
            ProgressBrush = UsageBrushes.FrozenBrush("#423C38");
            ShowsProgress = false;
            return;
        }

        var status = UsageStatus.FromRemainingPercent(usageWindow.IsBalance && usageWindow.Limit <= 0
            ? usageWindow.EffectiveRemaining > 0 ? 100 : 0
            : usageWindow.RemainingPercent);

        // Quota rows show the percent used, like Claude Code's /usage. A collector-supplied text,
        // such as a credit balance, is shown as it is.
        ValueText = string.IsNullOrWhiteSpace(usageWindow.RemainingText)
            ? $"{UsedPercent:0}% used"
            : usageWindow.RemainingText;
        LimitText = string.IsNullOrWhiteSpace(Detail) ? ValueText : Detail;
        ResetText = usageWindow.HideReset
            ? string.Empty
            : usageWindow.ResetAt is { } resetAt
            ? FormatResetText(resetAt)
            : FormatMissingResetText(usageWindow);
        ResetShortText = !usageWindow.HideReset && usageWindow.ResetAt is { } shortResetAt
            ? FormatResetShortText(shortResetAt)
            : string.Empty;
        ResetRelativeText = !usageWindow.HideReset && usageWindow.ResetAt is { } relativeResetAt
            ? FormatResetRelativeText(relativeResetAt)
            : string.Empty;
        ResetRelativeBrush = !usageWindow.HideReset && usageWindow.ResetAt is { } relativeBrushResetAt
            ? ResetRelativeBrushFor(relativeBrushResetAt)
            : UsageBrushes.FrozenBrush("#AEA69C");
        StatusLabel = status.Label;
        StatusBrush = status.Foreground;
        StatusBackground = status.Background;
        StatusIcon = status.Icon;
        ProgressBrush = status.Foreground;
    }

    public string Title { get; }

    public bool IsInactive { get; }

    public bool IsBalance { get; }

    public bool IsHighlightedBalance { get; }

    public bool ShowsProgress { get; }

    // True when the collector supplied the text (a balance or a money cap), not a percent.
    public bool HasCustomText { get; }

    public double Limit { get; }

    public double Used { get; }

    public double Remaining { get; }

    public double UsedPercent { get; }

    public double RemainingPercent { get; }

    public string ValueText { get; }

    public string LimitText { get; }

    public string ResetText { get; }

    public string ResetRelativeText { get; }

    // Time to the reset in a few characters, for the table: "35m", "4h", "2d", "now".
    public string ResetShortText { get; }

    public MediaBrush ResetRelativeBrush { get; }

    public string Detail { get; }

    public string StatusLabel { get; }

    public MediaBrush StatusBrush { get; }

    public MediaBrush StatusBackground { get; }

    public System.Windows.Media.ImageSource StatusIcon { get; }

    public MediaBrush ProgressBrush { get; }

    internal static string FormatResetShortText(DateTimeOffset resetAt, DateTimeOffset? now = null)
    {
        var remaining = resetAt - (now ?? DateTimeOffset.Now);
        if (remaining.TotalMinutes <= -1)
        {
            return "passed";
        }

        if (remaining.TotalMinutes < 1)
        {
            return "now";
        }

        if (remaining.TotalMinutes < 90)
        {
            return $"{Math.Max(1, (int)Math.Round(remaining.TotalMinutes))}m";
        }

        return remaining.TotalHours < 36
            ? $"{Math.Max(1, (int)Math.Round(remaining.TotalHours))}h"
            : $"{Math.Max(1, (int)Math.Round(remaining.TotalDays))}d";
    }

    private static string FormatResetRelativeText(DateTimeOffset resetAt)
    {
        var remaining = resetAt.ToLocalTime() - DateTimeOffset.Now;

        if (remaining.TotalMinutes <= -1)
        {
            return "reset passed";
        }

        if (remaining.TotalMinutes < 1)
        {
            return "now";
        }

        if (remaining.TotalMinutes < 90)
        {
            var minutes = Math.Max(1, (int)Math.Round(remaining.TotalMinutes));
            return minutes == 1 ? "in 1 minute" : $"in {minutes} minutes";
        }

        if (remaining.TotalHours < 36)
        {
            var hours = Math.Max(1, (int)Math.Round(remaining.TotalHours));
            return hours == 1 ? "in 1 hour" : $"in {hours} hours";
        }

        var days = Math.Max(1, (int)Math.Round(remaining.TotalDays));
        return days == 1 ? "in 1 day" : $"in {days} days";
    }

    private static MediaBrush ResetRelativeBrushFor(DateTimeOffset resetAt)
    {
        var remaining = resetAt.ToLocalTime() - DateTimeOffset.Now;

        if (remaining.TotalMinutes <= -1)
        {
            return UsageBrushes.FrozenBrush("#AEA69C");
        }

        if (remaining.TotalMinutes <= 0)
        {
            return UsageBrushes.FrozenBrush("#EF8A7A");
        }

        if (remaining.TotalHours <= 2)
        {
            return UsageBrushes.FrozenBrush("#E5B869");
        }

        return UsageBrushes.FrozenBrush("#8AB4F2");
    }

    private static string FormatResetText(DateTimeOffset resetAt)
    {
        var localResetAt = resetAt.ToLocalTime();
        var prefix = localResetAt <= DateTimeOffset.Now ? "Reset" : "Resets";
        return $"{prefix} {localResetAt:MMM d, h:mm tt}";
    }

    private static string FormatMissingResetText(UsageWindow usageWindow)
    {
        return usageWindow.RemainingPercent >= 99.9
            ? "No active reset"
            : "Reset time unavailable";
    }
}
