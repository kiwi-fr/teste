using System;
using System.IO;
using Microsoft.Win32;

namespace Coclico.InstallerAot.Services;

public static class RegistryService
{
    private const string UninstallKeyRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string CoclicoKey = "Coclico";

    public static void RegisterUninstallEntry(
        string displayName,
        string version,
        string installDir,
        string exePath)
    {
        using var key = Registry.LocalMachine.CreateSubKey(Path.Join(UninstallKeyRoot, CoclicoKey));
        if (key is null)
        {
            throw new InvalidOperationException("Impossible de creer la cle de registre de desinstallation.");
        }

        key.SetValue("DisplayName", displayName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("InstallLocation", installDir);
        var uninstaller = Path.Join(installDir, "unins000.exe");
        key.SetValue("UninstallString", $"\"{uninstaller}\"");
        key.SetValue("Publisher", "Coclico");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void RemoveUninstallEntry()
    {
        using var root = Registry.LocalMachine.OpenSubKey(UninstallKeyRoot, writable: true);
        root?.DeleteSubKey(CoclicoKey, throwOnMissingSubKey: false);
    }
}
