using System.Text.RegularExpressions;

namespace Coclico.Services;

internal static partial class AiActionParser
{
    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkBlockRegex();

    public static string Clean(string rawText)
    {
        return ThinkBlockRegex().Replace(rawText, string.Empty).Trim();
    }
}
