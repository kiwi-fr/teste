using Coclico.Services;
using Xunit;

namespace Coclico.Tests.Modules.Power;

/// <summary>
/// La garantie contractuelle de BatteryReportService est d'échouer proprement (retour null,
/// journal) plutôt que de lever une exception : machine fixe sans batterie, powercfg
/// indisponible, conteneur de services non construit. C'est ce que ces tests vérifient.
/// </summary>
public sealed class BatteryReportServiceTests
{
    [Fact]
    public async Task GenerateReportAsync_NeverThrows()
    {
        Exception? exception = await Record.ExceptionAsync(GenerateAsync);

        Assert.Null(exception);

        static async Task GenerateAsync()
        {
            string? reportPath = await BatteryReportService.GenerateReportAsync();
            if (reportPath != null)
            {
                // Si un rapport est produit, il doit exister sur le disque.
                Assert.True(File.Exists(reportPath));
            }
        }
    }

    [Fact]
    public void OpenReport_NonexistentPath_DoesNotThrow()
    {
        string nonexistent = Path.Combine(Path.GetTempPath(), "coclico-inexistant-" + Guid.NewGuid().ToString("N") + ".html");

        Exception? exception = Record.Exception(() => BatteryReportService.OpenReport(nonexistent));

        Assert.Null(exception);
    }
}
