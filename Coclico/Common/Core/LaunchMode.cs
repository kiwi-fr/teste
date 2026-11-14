namespace Coclico.Services;

internal enum LaunchMode { Normal, Minimized, Tray, Maximized }

internal static class LaunchModes
{
    public static bool IsValid(string? value)
    {
        return Enum.TryParse<LaunchMode>(value, ignoreCase: true, out LaunchMode mode)
            && Enum.IsDefined(mode);
    }
}
