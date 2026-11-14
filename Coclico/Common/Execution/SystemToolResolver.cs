using System.IO;

namespace Coclico.Services;

internal static class SystemToolResolver
{
    public static string Resolve(string toolFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolFileName);

        if (Path.IsPathRooted(toolFileName))
        {
            return toolFileName;
        }

        string system32Candidate = Path.Join(Environment.SystemDirectory, toolFileName);
        if (File.Exists(system32Candidate))
        {
            return system32Candidate;
        }

        string windowsCandidate = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Windows), toolFileName);
        if (File.Exists(windowsCandidate))
        {
            return windowsCandidate;
        }

        return toolFileName;
    }
}
