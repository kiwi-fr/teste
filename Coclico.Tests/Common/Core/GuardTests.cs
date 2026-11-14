using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class GuardTests
{
    private static readonly string[] AllowedGithubHosts = ["github.com", "objects.githubusercontent.com"];
    private static readonly string[] SingleGithubHost = ["github.com"];

    [Fact]
    public void NotNullOrWhiteSpaceThrowsOnEmpty()
    {
        Assert.Throws<ArgumentException>(() => Guard.NotNullOrWhiteSpace("", "p"));
        Assert.Throws<ArgumentException>(() => Guard.NotNullOrWhiteSpace(null, "p"));
        Assert.Throws<ArgumentException>(() => Guard.NotNullOrWhiteSpace("   ", "p"));
        Guard.NotNullOrWhiteSpace("ok", "p");
    }

    [Fact]
    public void EnsurePathInsideAcceptsChildPath()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "coclico-ut");
        string child = Path.Combine(baseDir, "fichier.json");

        string result = Guard.EnsurePathInside(baseDir, child, "path");

        Assert.Equal(Path.GetFullPath(child), result);
    }

    [Fact]
    public void EnsurePathInsideRejectsTraversal()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "coclico-ut");
        string escape = Path.Combine(baseDir, "..", "evil.json");

        Assert.Throws<ArgumentException>(() => Guard.EnsurePathInside(baseDir, escape, "path"));
    }

    [Fact]
    public void EnsurePathInsideRejectsSiblingWithCommonPrefix()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "coclico-ut");
        string sibling = Path.Combine(Path.GetTempPath(), "coclico-ut-evil", "x.json");

        Assert.Throws<ArgumentException>(() => Guard.EnsurePathInside(baseDir, sibling, "path"));
    }

    [Fact]
    public void EnsureHttpsUrlOnAllowedHostsAcceptsAllowedHttpsHost()
    {
        Uri uri = Guard.EnsureHttpsUrlOnAllowedHosts(
            "https://objects.githubusercontent.com/x/y.exe",
            AllowedGithubHosts,
            "url");

        Assert.Equal("objects.githubusercontent.com", uri.Host);
    }

    [Theory]
    [InlineData("http://github.com/x")]
    [InlineData("https://evil.example/x")]
    [InlineData("ftp://github.com/x")]
    [InlineData("pas-une-url")]
    [InlineData("")]
    public void EnsureHttpsUrlOnAllowedHostsRejectsBadUrls(string? url)
    {
        Assert.Throws<ArgumentException>(() =>
            Guard.EnsureHttpsUrlOnAllowedHosts(url, SingleGithubHost, nameof(url)));
    }

    [Fact]
    public void EnsureFileSizeLimitThrowsWhenFileTooBig()
    {
        string path = Path.Combine(Path.GetTempPath(), $"coclico-ut-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, new byte[2048]);

            Assert.Throws<IOException>(() => Guard.EnsureFileSizeLimit(path, 1024, "path"));
            Guard.EnsureFileSizeLimit(path, 4096, "path");
        }
        finally
        {
            File.Delete(path);
        }
    }
}