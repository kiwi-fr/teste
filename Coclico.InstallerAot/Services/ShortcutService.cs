using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Coclico.InstallerAot.Services;

/// <summary>
/// Cree les raccourcis .lnk (Bureau + Menu Demarrer).
///
/// ATTENTION NativeAOT : la creation classique de .lnk passe par COM
/// (WScript.Shell / IShellLink), et COM n'est PAS utilisable dans un
/// binaire NativeAOT (BuiltInComInteropSupport=false). La parade simple
/// et fiable : invoquer PowerShell en processus externe. C'est leger,
/// ca ne touche pas a votre exe, et Windows 10/11 a toujours PowerShell.
///
/// Alternative (plus propre a terme) : laisser Coclico creer ses
/// raccourcis au premier lancement (cote application, pas installeur).
/// </summary>
public static class ShortcutService
{
    public static Task CreateShortcutsAsync(string installDir, string exeName)
    {
        var exePath = Path.Combine(installDir, exeName);
        var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var startMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs", "Coclico");
        Directory.CreateDirectory(startMenuDir);

        var script =
            "$s = New-Object -ComObject WScript.Shell; " +
            $"$s.CreateShortcut('{Path.Combine(desktopDir, "Coclico.lnk")}').TargetPath = '{exePath}'; " +
            $"$s.CreateShortcut('{Path.Combine(startMenuDir, "Coclico.lnk")}').TargetPath = '{exePath}';";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossible de lancer PowerShell pour les raccourcis.");
        return process.WaitForExitAsync();
    }
}
