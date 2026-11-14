using Coclico.Services;
using Xunit;

namespace Coclico.Tests.Modules.Network;

public class PacketHeuristicsTests
{
    [Theory]
    [InlineData("google", false)]
    [InlineData("youtube", false)]
    [InlineData("wikipedia", false)]
    [InlineData("x7k9m2p4q8w1z3", true)]
    [InlineData("qwz8rty5uiop2as", true)]
    public void IsDgaLikeDetectsRandomNamesOnly(string label, bool expected)
    {
        Assert.Equal(expected, PacketHeuristics.IsDgaLike(label + ".com"));
    }

    [Fact]
    public void DomainScoreRandomHigherThanNormal()
    {
        double normal = PacketHeuristics.DomainScore("google.com");
        double dga = PacketHeuristics.DomainScore("x7k9m2p4q8w1z3.com");
        Assert.True(dga > normal, $"DGA ({dga:F2}) devrait dépasser normal ({normal:F2})");
        Assert.True(dga > 0.60, "DGA au-dessus du seuil 0.60");
        Assert.True(normal < 0.60, "domaine normal sous le seuil 0.60");
    }

    [Fact]
    public void ShannonEntropyUniformIsMaximal()
    {
        double h = PacketHeuristics.ShannonEntropy("abcdefghij");
        Assert.True(h > 3.0, "entropie de 10 caractères distincts > 3 bits");
        Assert.Equal(0, PacketHeuristics.ShannonEntropy(""));
        Assert.True(PacketHeuristics.ShannonEntropy("aaaa") < 0.01);
    }

    [Fact]
    public void BeaconRegularityRegularIntervalsScoreHigh()
    {
        double regular = PacketHeuristics.BeaconRegularity(new double[] { 1000, 1001, 999, 1000, 1002, 998 });
        double irregular = PacketHeuristics.BeaconRegularity(new double[] { 100, 5000, 300, 9000, 50, 7000 });
        Assert.True(regular > 0.95, $"régulier ~1 (obtenu {regular:F2})");
        Assert.True(irregular < 0.5, $"irrégulier < 0.5 (obtenu {irregular:F2})");
        Assert.Equal(0, PacketHeuristics.BeaconRegularity(new double[] { 1000, 1001 }));
    }

    [Fact]
    public void HeuristicVerdictBlocksDga()
    {
        var (v, _) = PacketHeuristics.HeuristicVerdict("x7k9m2p4q8w1z3.com", 0);
        Assert.Equal(PacketVerdict.Block, v);
    }

    [Fact]
    public void HeuristicVerdictBlocksRegularBeaconToSuspiciousHost()
    {
        var (v, _) = PacketHeuristics.HeuristicVerdict("x7k9m2p4q8w1z3.com", 0.95);
        Assert.Equal(PacketVerdict.Block, v);
    }

    [Fact]
    public void HeuristicVerdictAllowsNormalHost()
    {
        var (v, _) = PacketHeuristics.HeuristicVerdict("google.com", 0.1);
        Assert.Equal(PacketVerdict.Allow, v);
    }

    [Fact]
    public void GetDnsRcodeNxdomain()
    {
        byte[] dns = new byte[12];
        dns[3] = 0x83;
        Assert.Equal(3, PacketParser.GetDnsRcode(dns, false));
    }

    [Fact]
    public void ContainsAdTokenDetectsTokens()
    {
        byte[] p = System.Text.Encoding.ASCII.GetBytes("GET /pixel/track HTTP/1.1");
        Assert.True(PacketParser.ContainsAdToken(p));
        byte[] clean = System.Text.Encoding.ASCII.GetBytes("GET /index.html HTTP/1.1");
        Assert.False(PacketParser.ContainsAdToken(clean));
    }
}
