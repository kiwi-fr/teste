using System.Reflection;
using System.Runtime.InteropServices;

namespace Coclico.Installer;

internal static class NativeLibBundle
{
    /// <summary>Extrait les DLL natives embarquées vers %TEMP% et les pré-charge
    /// pour que l'exe fonctionne seul, sans les fichiers .dll à côté.</summary>
    public static void ExtractAndLoad()
    {
        string dir = Path.Combine(Path.GetTempPath(), "CoclicoInstaller");
        Directory.CreateDirectory(dir);

        string[] dlls = { "libSkiaSharp.dll", "libHarfBuzzSharp.dll", "av_libglesv2.dll" };

        foreach (string name in dlls)
        {
            string target = Path.Combine(dir, name);
            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                if (stream is null) continue;
                using var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
                stream.CopyTo(fs);
            }
            catch (IOException) { /* verrouillé par une autre instance déjà lancée */ }

            try { NativeLibrary.Load(target); } catch { /* déjà chargée */ }
        }
    }
}