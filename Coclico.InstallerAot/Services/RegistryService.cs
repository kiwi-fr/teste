using System;
using System.IO;
using Microsoft.Win32;

namespace Coclico.InstallerAot.Services;

/// <summary>
/// Cree l'entree "Ajouter/Supprimer des programmes" (Apps & Features),
/// equivalente a celle de votre installeur WPF actuel.
///
/// Compatibilite NativeAOT : Microsoft.Win32.Registry est base sur des
/// appels natifs (P/Invoke), sans reflexion -> compatible AOT/trimming.
/// </summary>
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
        using var key = Registry.LocalMachine.CreateSubKey(Path.Combine(UninstallKeyRoot, CoclicoKey));
        if (key is null)
        {
            throw new InvalidOperationException("Impossible de creer la cle de registre de desinstallation.");
        }

        key.SetValue("DisplayName", displayName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("InstallLocation", installDir);

        // Desinstallation : Coclico gere sa propre desinstallation
        // (comme dans votre version WPF). Pour le prototype, on pointe
        // vers unins000.exe genere par l'app — a adapter selon votre
        // mecanisme actuel (UninstallWindow).
        var uninstaller = Path.Combine(installDir, "unins000.exe");
        key.SetValue("UninstallString", $"\"{uninstaller}\"");
        key.SetValue("Publisher", "Coclico");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>Appelable par un futur desinstalleur pour retirer l'entree.</summary>
    public static void RemoveUninstallEntry()
    {
        using var root = Registry.LocalMachine.OpenSubKey(UninstallKeyRoot, writable: true);
        root?.DeleteSubKey(CoclicoKey, throwOnMissingSubKey: false);
    }
}
