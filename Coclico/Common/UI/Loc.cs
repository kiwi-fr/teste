using System.Windows;

namespace Coclico.Services;

internal static class Loc
{
    public static string Get(string key)
    {
        try
        {
            return Application.Current?.TryFindResource(key) as string
                ?? ServiceContainer.GetOptional<LocalizationService>()?.Get(key)
                ?? key;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LoggingService.LogException(ex, "Loc.Get");
            return key;
        }
    }

    public static string Format(string key, params object?[] args)
    {
        return TextFormatService.Format(System.Globalization.CultureInfo.InvariantCulture, Get(key), args);
    }
}
