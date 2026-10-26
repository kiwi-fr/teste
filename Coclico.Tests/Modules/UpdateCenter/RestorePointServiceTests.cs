using Coclico.Modules.UpdateCenter.Services;
using Xunit;

namespace Coclico.Tests.Modules.UpdateCenter;

/// <summary>
/// Validation des arguments de RestorePointService.Create.
/// Le chemin valide n'est PAS appelé ici : créer un vrai point de restauration
/// pendant un test modifierait la machine de l'utilisateur.
/// </summary>
public sealed class RestorePointServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyDescription_Throws(string? description)
    {
        var service = new RestorePointService();

        _ = Assert.Throws<ArgumentException>(() => service.Create(description!));
    }

    [Fact]
    public void Create_DescriptionLongerThan64Chars_Throws()
    {
        var service = new RestorePointService();
        string tooLong = new string('a', 65);

        _ = Assert.Throws<ArgumentException>(() => service.Create(tooLong));
    }
}
