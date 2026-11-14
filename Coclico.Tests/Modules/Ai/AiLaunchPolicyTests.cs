using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class AiLaunchPolicyTests
{
    [Theory]
    [InlineData("notepad")]
    [InlineData("notepad.exe")]
    [InlineData("calc")]
    [InlineData("mspaint")]
    [InlineData("taskmgr")]
    [InlineData("cleanmgr")]
    [InlineData("ms-settings:display")]
    public void IsAiLaunchAllowedAcceptsStandardAppsAndSettingsUris(string target)
    {
        Assert.True(new SecurityPolicyService().IsAiLaunchAllowed(target));
    }

    [Theory]
    [InlineData("cmd")]
    [InlineData("cmd.exe")]
    [InlineData("powershell")]
    [InlineData("powershell.exe")]
    [InlineData("pwsh")]
    [InlineData("wt")]
    [InlineData("conhost")]
    [InlineData("regedit")]
    [InlineData("reg")]
    [InlineData("taskkill")]
    [InlineData("mshta")]
    [InlineData("rundll32")]
    [InlineData("cscript")]
    [InlineData("wscript")]
    public void IsAiLaunchAllowedRejectsShellsAndScriptHosts(string target)
    {
        Assert.False(new SecurityPolicyService().IsAiLaunchAllowed(target));
    }

    [Theory]
    [InlineData(@"C:\ProgramData\Evil\evil.exe")]
    [InlineData(@"C:\Users\user\AppData\Local\Programs\app\app.exe")]
    [InlineData(@"C:\Users\user\Downloads\tool.exe")]
    [InlineData(@"C:\Temp\payload.exe")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAiLaunchAllowedRejectsUnapprovedOrMissingPaths(string target)
    {
        Assert.False(new SecurityPolicyService().IsAiLaunchAllowed(target));
    }

    [Theory]
    [InlineData("ms-settings:;format c:")]
    [InlineData("ms-settings:display|calc")]
    [InlineData("notepad && calc")]
    [InlineData("ms-settings:")]
    public void IsAiLaunchAllowedRejectsMalformedSettingsUrisAndInjectedParameters(string target)
    {
        Assert.False(new SecurityPolicyService().IsAiLaunchAllowed(target));
    }
}
