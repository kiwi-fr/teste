using Coclico.Services;
using Xunit;

namespace Coclico.Tests.Common.Core;

public sealed class SettingsServiceExportImportTests : IDisposable
{
    private readonly string _tempDir = Path.Join(Path.GetTempPath(), "coclico-tests-" + Guid.NewGuid().ToString("N"));

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
            return;
        }
    }

    [Fact]
    public async Task ExportToAsyncWritesValidJsonFile()
    {
        var service = new SettingsService();
        service.Settings.Language = "en";
        string path = Path.Join(_tempDir, "export.json");

        bool success = await service.ExportToAsync(path);

        Assert.True(success);
        Assert.True(File.Exists(path));
        string json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("\"language\"", json, StringComparison.Ordinal);
        Assert.Contains("\"en\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportToAsyncEmptyPathReturnsFalse()
    {
        var service = new SettingsService();

        bool success = await service.ExportToAsync(" ");

        Assert.False(success);
    }

    [Fact]
    public async Task ImportFromAsyncMissingFileReturnsFalse()
    {
        var service = new SettingsService();

        bool success = await service.ImportFromAsync(Path.Join(_tempDir, "inexistant.json"));

        Assert.False(success);
    }

    [Fact]
    public async Task ImportFromAsyncRoundTripAppliesExportedSettings()
    {
        var exporter = new SettingsService();
        exporter.Settings.Language = "en";
        string path = Path.Join(_tempDir, "roundtrip.json");
        Assert.True(await exporter.ExportToAsync(path));

        var importer = new SettingsService();
        Assert.NotEqual("en", importer.Settings.Language);

        bool success = await importer.ImportFromAsync(path);

        Assert.True(success);
        Assert.Equal("en", importer.Settings.Language);
    }

    [Fact]
    public async Task ImportFromAsyncPreservesInstallationFields()
    {
        string path = Path.Join(_tempDir, "installation-fields.json");
        await File.WriteAllTextAsync(path, "{ \"firstRun\": true, \"hasSeenWelcome\": true, \"language\": \"en\" }", TestContext.Current.CancellationToken);

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
    public async Task ImportFromAsyncInvalidJsonReturnsFalse()
    {
        string path = Path.Join(_tempDir, "invalide.json");
        await File.WriteAllTextAsync(path, "ceci n'est pas du json", TestContext.Current.CancellationToken);

        var service = new SettingsService();

        bool success = await service.ImportFromAsync(path);

        Assert.False(success);
    }

    [Fact]
    public async Task ImportFromAsyncUnknownLanguageIsSanitized()
    {
        string path = Path.Join(_tempDir, "langue-invalide.json");
        await File.WriteAllTextAsync(path, "{ \"language\": \"zz\" }", TestContext.Current.CancellationToken);

        var service = new SettingsService();

        bool success = await service.ImportFromAsync(path);

        Assert.True(success);
        Assert.Equal("fr", service.Settings.Language);
    }
}
