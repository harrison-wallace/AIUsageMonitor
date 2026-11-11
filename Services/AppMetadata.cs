namespace AIUsageMonitor.Services;

internal static class AppMetadata
{
    public const string DisplayName = "AI Usage Monitor";
    public const string VersionText = "V1.21.0";
    public const string StartupEntryName = "AIUsageMonitor";

    public static string DisplayNameWithVersion => $"{DisplayName} {VersionText}";
}
