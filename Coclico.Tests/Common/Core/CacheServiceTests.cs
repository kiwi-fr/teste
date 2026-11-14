using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CacheServiceTests : IAsyncLifetime
{
    private readonly CacheService _cache = new();
    private readonly List<string> _keys = [];

    private string Key(string name)
    {
        string key = $"ut-cache-{Guid.NewGuid():N}-{name}";
        _keys.Add(key);
        return key;
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string key in _keys)
        {
            await _cache.InvalidateAsync(key).ConfigureAwait(false);
        }

        await _cache.ClearSubdirAsync("ut-subdir").ConfigureAwait(false);
        await _cache.ClearSubdirAsync("ut-clear-a").ConfigureAwait(false);
        await _cache.ClearSubdirAsync("ut-clear-b").ConfigureAwait(false);
    }

    private sealed record SamplePayload(string Name, int Count, bool Flag);

    [Fact]
    public async Task SetThenGetAsyncRoundTripsComplexObject()
    {
        string key = Key("roundtrip");
        var payload = new SamplePayload("nettoyage", 42, true);

        await _cache.SetAsync(key, payload);
        SamplePayload? readBack = await _cache.GetAsync<SamplePayload>(key);

        Assert.NotNull(readBack);
        Assert.Equal(payload, readBack);
    }

    [Fact]
    public async Task GetAsyncMissingKeyReturnsDefault()
    {
        string key = Key("absent");

        Assert.Null(await _cache.GetAsync<SamplePayload>(key));
        Assert.False(await _cache.HasAsync(key));
    }

    [Fact]
    public async Task HasAsyncTrueAfterSetFalseAfterInvalidate()
    {
        string key = Key("has");

        await _cache.SetAsync(key, "valeur");
        Assert.True(await _cache.HasAsync(key));

        await _cache.InvalidateAsync(key);
        Assert.False(await _cache.HasAsync(key));
        Assert.Null(await _cache.GetAsync<string>(key));
    }

    [Fact]
    public async Task TryGetAsyncReturnsFalseForExpiredEntry()
    {
        string key = Key("ttl");
        await _cache.SetAsync(key, "périmé", TimeSpan.FromMilliseconds(1));

        await Task.Delay(100, TestContext.Current.CancellationToken);

        (bool found, string? value) = await _cache.TryGetAsync<string>(key);
        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public async Task TryGetAsyncReturnsTrueForFreshEntry()
    {
        string key = Key("fresh");
        await _cache.SetAsync(key, "frais", TimeSpan.FromMinutes(5));

        (bool found, string? value) = await _cache.TryGetAsync<string>(key);
        Assert.True(found);
        Assert.Equal("frais", value);
    }

    [Fact]
    public async Task GetOrSetAsyncCallsFactoryOnceThenServesFromCache()
    {
        string key = Key("factory");
        int calls = 0;

        Task<string> Factory()
        {
            calls++;
            return Task.FromResult(calls == 1 ? "généré" : "généré-autre");
        }

        string first = await _cache.GetOrSetAsync(key, Factory);
        string second = await _cache.GetOrSetAsync(key, Factory);

        Assert.Equal("généré", first);
        Assert.Equal("généré", second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetOrSetAsyncConcurrentCallsExecuteFactoryOnlyOnce()
    {
        string key = Key("race");
        int calls = 0;

        async Task<string> Factory()
        {
            _ = Interlocked.Increment(ref calls);
            await Task.Delay(50).ConfigureAwait(false);
            return "valeur";
        }

        const int concurrency = 16;
        Task<string>[] tasks = Enumerable.Range(0, concurrency)
            .Select(_ => _cache.GetOrSetAsync(key, Factory))
            .ToArray();

        string[] results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("valeur", r));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SubdirVariantRoundTripsIndependentlyFromRootKey()
    {
        string key = Key("sub");
        string rootKey = Key("root");
        const string subdir = "ut-subdir";

        await _cache.SetAsync(rootKey, "racine");
        await _cache.SetInSubdirAsync(subdir, key, "sous-dossier");

        Assert.Equal("sous-dossier", await _cache.GetFromSubdirAsync<string>(subdir, key));
        Assert.Equal("racine", await _cache.GetAsync<string>(rootKey));

        await _cache.InvalidateInSubdirAsync(subdir, key);
        Assert.Null(await _cache.GetFromSubdirAsync<string>(subdir, key));
        Assert.Equal("racine", await _cache.GetAsync<string>(rootKey));
    }

    [Fact]
    public async Task TryGetAsyncCorruptedEntryIsTreatedAsMissing()
    {
        string key = Key("corrupt");
        string path = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "cache", key + ".cache.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ ceci n'est pas du json valide", TestContext.Current.CancellationToken);

        (bool found, string? value) = await _cache.TryGetAsync<string>(key);
        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public async Task KeysWithInvalidFileNameCharactersAreSanitizedConsistently()
    {
        string key = Key("invalid") + ":*?";

        await _cache.SetAsync(key, "sanitisé");

        Assert.True(await _cache.HasAsync(key));
        Assert.Equal("sanitisé", await _cache.GetAsync<string>(key));
    }

    [Fact]
    public async Task SetThenGetAsyncRoundTripsValue()
    {
        string key = Key("async");

        await _cache.SetAsync(key, new SamplePayload("async", 1, false));
        (bool found, SamplePayload? value) = await _cache.TryGetAsync<SamplePayload>(key);

        Assert.True(found);
        Assert.NotNull(value);
        Assert.Equal("async", value.Name);
        Assert.Equal(1, value.Count);
    }

    [Fact]
    public async Task ClearSubdirAsyncRemovesOnlyTargetedEntries()
    {
        string subdirA = "ut-clear-a";
        string subdirB = "ut-clear-b";
        string keyA = Key("a");
        string keyB = Key("b");

        await _cache.SetInSubdirAsync(subdirA, keyA, "A");
        await _cache.SetInSubdirAsync(subdirB, keyB, "B");

        await _cache.ClearSubdirAsync(subdirA);

        Assert.Null(await _cache.GetFromSubdirAsync<string>(subdirA, keyA));
        Assert.Equal("B", await _cache.GetFromSubdirAsync<string>(subdirB, keyB));
    }

    [Fact]
    public async Task JsonEntriesAreNotIndentedToKeepFilesSmall()
    {
        string key = Key("indent");
        await _cache.SetAsync(key, "compact");

        string path = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "cache", key + ".cache.json");

        Assert.True(File.Exists(path));
        string json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("\n  ", json, StringComparison.Ordinal);
        Assert.Contains("ExpiresAt", json, StringComparison.Ordinal);
    }
}
