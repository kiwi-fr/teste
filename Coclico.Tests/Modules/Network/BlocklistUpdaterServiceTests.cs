using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class BlocklistUpdaterServiceTests
{
    [Theory]
    [InlineData("0.0.0.0 ads.example.com", "ads.example.com")]
    [InlineData("127.0.0.1 tracker.example.org", "tracker.example.org")]
    [InlineData("::1  doh.example.net", "doh.example.net")]
    [InlineData("doubleclick.net", "doubleclick.net")]
    [InlineData("||ads.example.com^", "ads.example.com")]
    [InlineData("  # commentaire", null)]
    [InlineData("! commentaire adblock", null)]
    [InlineData("[Adblock Plus 2.0]", null)]
    [InlineData("0.0.0.0", null)]
    [InlineData("0.0.0.0 *.wildcard.com", null)]
    [InlineData("", null)]
    [InlineData("not a domain at all !!", null)]
    [InlineData("0.0.0.0 ADS.Example.COM", "ads.example.com")]
    public void ParseHostsLineExtractsDomain(string line, string? expected)
    {
        Assert.Equal(expected, BlocklistUpdaterService.ParseHostsLine(line));
    }

    [Fact]
    public void ParseListMergesAndDedupes()
    {
        string list = """
            # HaGeZi test list
            0.0.0.0 ads.example.com
            127.0.0.1 tracker.example.com
            0.0.0.0 ads.example.com
            ||doubleclick.net^
            ! comment
            0.0.0.0
            """;

        var set = BlocklistUpdaterService.ParseList(list);

        Assert.Equal(3, set.Count);
        Assert.Contains("ads.example.com", set, StringComparison.Ordinal);
        Assert.Contains("tracker.example.com", set, StringComparison.Ordinal);
        Assert.Contains("doubleclick.net", set, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultListUrlsAreWellFormed()
    {
        Assert.All(BlocklistUpdaterService.DefaultListUrls, u =>
        {
            Assert.StartsWith("https://", u, StringComparison.Ordinal);
        });
        Assert.Contains(BlocklistUpdaterService.DefaultListUrls, u => u.Contains("hagezi", StringComparison.Ordinal));
        Assert.Contains(BlocklistUpdaterService.DefaultListUrls, u => u.Contains("oisd", StringComparison.Ordinal));
    }
}
