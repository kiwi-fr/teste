using System.IO;

namespace Coclico.Security.Infrastructure;

public static class MarkOfTheWeb
{
    private const string ZoneIdentifierSuffix = ":Zone.Identifier";
    private const string ZoneIdentifierContent = "[ZoneTransfer]\r\nZoneId=3\r\n";

    public static void Apply(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.WriteAllText(filePath + ZoneIdentifierSuffix, ZoneIdentifierContent);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"[MarkOfTheWeb] Écriture du Zone.Identifier impossible pour {filePath} : {ex.Message}");
        }
    }
}
