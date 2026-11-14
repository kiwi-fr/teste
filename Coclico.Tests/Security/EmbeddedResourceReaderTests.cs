using System.Text;
using Coclico.Security.Infrastructure;
using Xunit;

namespace Coclico.Tests;

public sealed class EmbeddedResourceReaderTests
{
    private const string TestResourceName = "Coclico.Tests.TestEmbeddedResource";

    [Fact]
    public void ReadResourceReadsOwnAssemblyEmbeddedResource()
    {
        string assemblyPath = typeof(EmbeddedResourceReaderTests).Assembly.Location;

        byte[]? bytes = EmbeddedResourceReader.ReadResource(assemblyPath, TestResourceName);

        Assert.NotNull(bytes);
        Assert.Equal("coclico-test-resource-v1", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void ReadResourceUnknownNameReturnsNull()
    {
        string assemblyPath = typeof(EmbeddedResourceReaderTests).Assembly.Location;

        Assert.Null(EmbeddedResourceReader.ReadResource(assemblyPath, "Coclico.Tests.DoesNotExist"));
    }

    [Fact]
    public void ReadResourceNotAPeFileReturnsNull()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"not-a-pe-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(temp, "hello, pas un PE");
            Assert.Null(EmbeddedResourceReader.ReadResource(temp, TestResourceName));
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
