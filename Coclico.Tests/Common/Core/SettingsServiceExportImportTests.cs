using Coclico.Services;
using Xunit;

namespace Coclico.Tests.Common.Core;

/// <summary>
/// Tests de l'export/import des réglages (SettingsService.ExportToAsync / ImportFromAsync).
/// </summary>
public sealed class SettingsServiceExportImportTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "coclico-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsServiceExportImportTests()
    {
        _ = Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Le nettoyage des fichiers temporaires n'est pas l'objet du test.
        }
    }

    [Fact]
    public async Task ExportToAsync_WritesValidJsonFile()
    {
        var service = new SettingsService();
        service.Settings.Language = "en";
        string path = Path.Combine(_tempDir, "export.json");

        bool success = await service.ExportToAsync(path);

        Assert.True(success);
        Assert.True(File.Exists(path));
        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"language\"", json);
        Assert.Contains("\"en\"", json);
    }

    [Fact]
    public async Task ExportToAsync_EmptyPath_ReturnsFalse()
    {
        var service = new SettingsService();

        bool success = await service.ExportToAsync(" ");

        Assert.False(success);
    }

    [Fact]
    public async Task ImportFromAsync_MissingFile_ReturnsFalse()
    {
        var service = new SettingsService();

        bool success = await service.ImportFromAsync(Path.Combine(_tempDir, "inexistant.json"));

        Assert.False(success);
    }

    [Fact]
    public async Task ImportFromAsync_RoundTrip_AppliesExportedSettings()
    {
        var exporter = new SettingsService();
        exporter.Settings.Language = "en";
        string path = Path.Combine(_tempDir, "roundtrip.json");
        Assert.True(await exporter.ExportToAsync(path));

        var importer = new SettingsService();
        Assert.NotEqual("en", importer.Settings.Language);

        bool success = await importer.ImportFromAsync(path);

        Assert.True(success);
        Assert.Equal("en", importer.Settings.Language);
    }

    [Fact]
    public async Task ImportFromAsync_PreservesInstallationFields()
    {
        string path = Path.Combine(_tempDir, "installation-fields.json");
        await File.WriteAllTextAsync(path, /*lang=json,strict*/ "{ \"firstRun\": true, \"hasSeenWelcome\": true, \"language\": \"en\" }");

        var importer = new SettingsService();
        importer.Settings.FirstRun = false;
        importer.Settings.HasSeenWelcome = false;

        bool success = await importer.ImportFromAsync(path);

        Assert.True(success);
        Assert.False(importer.Settings.FirstRun);
        Assert.False(importer.Settings.HasSeenWelcome);
        Assert.Equal("en", importer.Settings.Language);
    }

    [Fact]
    public async Task ImportFromAsync_InvalidJson_ReturnsFalse()
    {
        string path = Path.Combine(_tempDir, "invalide.json");
        await File.WriteAllTextAsync(path, "ceci n'est pas du json");

        var service = new SettingsService();

        bool success = await service.ImportFromAsync(path);

        Assert.False(success);
    }

    [Fact]
    public async Task ImportFromAsync_UnknownLanguage_IsSanitized()
    {
        string path = Path.Combine(_tempDir, "langue-invalide.json");
        await File.WriteAllTextAsync(path, /*lang=json,strict*/ "{ \"language\": \"zz\" }");

        var service = new SettingsService();

        bool success = await service.ImportFromAsync(path);

        Assert.True(success);
        Assert.Equal("fr", service.Settings.Language);
    }
}
