using System.Text.RegularExpressions;
using Coclico.Security.Infrastructure;
using Xunit;

namespace Coclico.Tests;

public sealed class SecurityPropertyTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "coclico-proptests-" + Guid.NewGuid().ToString("N"));

    public SecurityPropertyTests()
    {
        _ = Directory.CreateDirectory(_dir);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("coclico")]
    [InlineData("contenu avec accents: sécurité")]
    public void Sha256OfFileAlwaysMatches64LowercaseHexDigits(string? content)
    {
        string path = Path.Join(_dir, "sha-input.bin");
        File.WriteAllText(path, content ?? string.Empty);
        Assert.Matches("^[0-9a-f]{64}$", FileIntegrityScanner.Sha256File(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("safe/file.txt")]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("\\absolute")]
    [InlineData("C:\\escape")]
    [InlineData("folder:stream")]
    public void PathsAcceptedByValidatorNeverContainColonOrLeadingSeparator(string? candidate)
    {
        if (candidate is null || !PathValidator.IsSafeRelativePath(candidate))
        {
            return;
        }

        Assert.DoesNotContain(':', candidate);
        Assert.False(candidate.StartsWith('/') || candidate.StartsWith('\\'));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("safe/file.txt")]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("\\absolute")]
    [InlineData("C:\\escape")]
    [InlineData("folder:stream")]
    public void EntriesAcceptedByArchiveGuardNeverContainColonOrLeadingSeparator(string? candidate)
    {
        if (candidate is null || !ArchiveGuard.IsSafeArchiveEntry(candidate))
        {
            return;
        }

        Assert.DoesNotContain(':', candidate);
        Assert.False(candidate.StartsWith('/') || candidate.StartsWith('\\'));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }
}
