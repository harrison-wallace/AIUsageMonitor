using AIUsageMonitor.Models;
using AIUsageMonitor.ViewModels;

namespace AIUsageMonitor.Tests;

[TestClass]
public sealed class ProviderLogosTests
{
    [TestMethod]
    [DataRow("Anthropic - Work", "Claude")]
    [DataRow("Anthropic API Credits", "Anthropic")]
    [DataRow("OpenAI - Personal", "OpenAI")]
    [DataRow("GitHub Copilot", "GitHub Copilot")]
    [DataRow("Gemini", "Gemini")]
    [DataRow("Cursor", "Cursor")]
    public void EachProviderGetsItsLogo(string name, string brand)
    {
        Assert.AreEqual(brand, ProviderLogos.For(name)?.Brand);
    }

    [TestMethod]
    public void AProviderWithNoLogoKeepsItsName()
    {
        Assert.IsNull(ProviderLogos.For(KnownProviders.Antigravity));
        Assert.AreEqual("Antigravity - 22%", ProviderLogos.StripPrefix(null, "Antigravity - 22%"));
    }

    [TestMethod]
    [DataRow("Anthropic - Work - 68% used", "Work - 68% used")]
    [DataRow("Anthropic API", "API")]
    [DataRow("Fable - Personal - 65%", "Fable - Personal - 65%")]
    public void TheLogoReplacesOnlyTheProviderPrefix(string text, string expected)
    {
        Assert.AreEqual(expected, ProviderLogos.StripPrefix(ProviderLogos.For("Anthropic"), text));
    }
}
