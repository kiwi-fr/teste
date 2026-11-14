namespace Coclico.Services;

internal interface ISecurityPolicy
{
    bool IsCommandBlocked(string normalizedCmd);

    bool IsPowerShellBlocked(string normalizedPs);

    bool IsProtectedPath(string lowerCasePath);

    bool IsApplicationAllowed(string appName, out string? resolvedPath);

    bool IsAiLaunchAllowed(string targetPath);

    string? PolicyFilePath { get; }

    Task ReloadAsync();
}
