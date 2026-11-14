using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class LocTests
{
    [Fact]
    public void GetUnknownKeyReturnsTheKeyItself()
    {
        Assert.Equal("cle_inexistante_xyz", Loc.Get("cle_inexistante_xyz"));
    }

    [Fact]
    public void FormatUnknownKeyFormatsTheKeyWithArgs()
    {
        string result = Loc.Format("cle_inexistante_{0}", 42);

        Assert.Equal("cle_inexistante_42", result);
    }
}
