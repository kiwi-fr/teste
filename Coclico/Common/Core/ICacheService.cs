namespace Coclico.Services;

internal interface ICacheService
{
    long GetCacheSizeBytes();

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null);
    Task<T?> GetAsync<T>(string key);
    Task<(bool Found, T? Value)> TryGetAsync<T>(string key);
    Task<bool> HasAsync(string key);
    Task InvalidateAsync(string key);
    Task ClearAsync();

    Task SetInSubdirAsync<T>(string subdir, string key, T value, TimeSpan? ttl = null);
    Task<T?> GetFromSubdirAsync<T>(string subdir, string key);
    Task InvalidateInSubdirAsync(string subdir, string key);
    Task ClearSubdirAsync(string subdir);
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null);
}
