using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;

namespace AIUsageMonitor.ViewModels;

// VortexOne colours (vortexone packages/ui/src/styles/tokens.css). Dark only.
internal static class UsageBrushes
{
    public static readonly MediaBrush Canvas = FrozenBrush("#2B2622");
    public static readonly MediaBrush Ink = FrozenBrush("#F7F5F0");
    public static readonly MediaBrush Mute = FrozenBrush("#AEA69C");
    public static readonly MediaBrush CanvasRaised = FrozenBrush("#423C38");

    // Categorical slots in fixed order; colour follows the provider.
    private static readonly MediaBrush AnthropicAccent = FrozenBrush("#D9663A"); // cat-2
    private static readonly MediaBrush OpenAiAccent = FrozenBrush("#22A37A"); // cat-3
    private static readonly MediaBrush AntigravityAccent = FrozenBrush("#D15E8C"); // cat-5
    private static readonly MediaBrush GeminiAccent = FrozenBrush("#4A8FE0"); // cat-1
    private static readonly MediaBrush CursorAccent = FrozenBrush("#9085E9"); // cat-7
    private static readonly MediaBrush CopilotAccent = FrozenBrush("#4F9A2E"); // cat-6
    private static readonly MediaBrush DefaultAccent = FrozenBrush("#DAD2C1"); // cat-other

    public static MediaBrush ProviderAccent(string providerName)
    {
        var normalized = providerName.Trim().ToLowerInvariant();

        if (normalized.StartsWith("anthropic"))
        {
            return AnthropicAccent;
        }

        if (normalized.StartsWith("openai"))
        {
            return OpenAiAccent;
        }

        if (normalized.StartsWith("antigravity"))
        {
            return AntigravityAccent;
        }

        if (normalized.StartsWith("gemini"))
        {
            return GeminiAccent;
        }

        if (normalized.StartsWith("github copilot"))
        {
            return CopilotAccent;
        }

        return normalized.StartsWith("cursor") ? CursorAccent : DefaultAccent;
    }

    public static MediaBrush FrozenBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
