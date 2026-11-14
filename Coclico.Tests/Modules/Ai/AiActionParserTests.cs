using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class AiActionParserTests
{
    private const string Open = "<" + "think" + ">";
    private const string Close = "</" + "think" + ">";

    [Fact]
    public void CleanRemovesThinkBlock()
    {
        string raw = Open + "raisonnement privé" + Close + "Réponse finale";
        Assert.Equal("Réponse finale", AiActionParser.Clean(raw));
    }

    [Fact]
    public void CleanRemovesMultilineThinkBlock()
    {
        string raw = Open + "ligne 1\nligne 2\nligne 3" + Close + "\nRéponse utile";
        Assert.Equal("Réponse utile", AiActionParser.Clean(raw));
    }

    [Fact]
    public void CleanRemovesMultipleThinkBlocks()
    {
        string raw = Open + "premier raisonnement" + Close + "Réponse A"
                   + Open + "second raisonnement" + Close + " et suite";
        Assert.Equal("Réponse A et suite", AiActionParser.Clean(raw));
    }

    [Fact]
    public void CleanIsCaseInsensitiveOnTags()
    {
        string raw = Open.ToUpperInvariant() + "raisonnement" + Close.ToUpperInvariant() + "Réponse publique";
        Assert.Equal("Réponse publique", AiActionParser.Clean(raw));
    }

    [Fact]
    public void CleanTrimsSurroundingWhitespace()
    {
        Assert.Equal("Bonjour", AiActionParser.Clean("   Bonjour   "));
    }

    [Fact]
    public void CleanWithoutThinkBlockReturnsTrimmedInput()
    {
        Assert.Equal("Texte normal", AiActionParser.Clean("Texte normal"));
    }

    [Fact]
    public void CleanEmptyInputReturnsEmpty()
    {
        Assert.Equal(string.Empty, AiActionParser.Clean(""));
        Assert.Equal(string.Empty, AiActionParser.Clean("   "));
    }

    [Fact]
    public void CleanNullInputThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AiActionParser.Clean(null!));
    }
}
