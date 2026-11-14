using System.Collections.Concurrent;
using System.IO;
using Serilog;

namespace Coclico.Services;

internal static class LoggingService
{
    private static readonly string LogDir = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Coclico", "logs");

    private static readonly Lock _configLock = new();
    private static bool _serilogConfigured;

    private static readonly ConcurrentDictionary<string, DateTime> _lastLogUtc = new(StringComparer.Ordinal);
    private static readonly TimeSpan SuppressWindow = TimeSpan.FromSeconds(5);
    private const int MaxTrackedMessages = 4096;

    private static bool TryAcquireLogSlot(string level, string message)
    {
        try
        {
            if (_lastLogUtc.Count > MaxTrackedMessages)
            {
                _lastLogUtc.Clear();
            }

            DateTime now = DateTime.UtcNow;
            string key = level + "|" + message;
            DateTime last = _lastLogUtc.GetOrAdd(key, now);
            if (now - last < SuppressWindow)
            {
                return false;
            }

            _lastLogUtc[key] = now;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _ = ex;
            return true;
        }
    }

    static LoggingService()
    {
        EnsureSerilog();
    }

    public static void EnsureSerilog()
    {
        if (_serilogConfigured)
        {
            return;
        }

        lock (_configLock)
        {
            if (_serilogConfigured)
            {
                return;
            }

            try
            {
                _ = Directory.CreateDirectory(LogDir);
                PurgeLegacyLogs();
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }

            Log.Logger = new LoggerConfiguration()
#if DEBUG
                .MinimumLevel.Debug()
#else
                .MinimumLevel.Information()
#endif
                .WriteTo.File(
                    Path.Join(LogDir, "coclico-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .Enrich.FromLogContext()
                .CreateLogger();

            _serilogConfigured = true;
        }
    }

    public static void LogInfo(string message)
    {
        if (!TryAcquireLogSlot("I", message))
        {
            return;
        }

        try { Log.Information("{Message}", message); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
    }

    public static void LogWarning(string message)
    {
        if (!TryAcquireLogSlot("W", message))
        {
            return;
        }

        try { Log.Warning("{Message}", message); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
    }

    public static void LogError(string message)
    {
        if (!TryAcquireLogSlot("E", message))
        {
            return;
        }

        try { Log.Error("{Message}", message); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
    }

    public static void LogDebug(string message)
    {
        if (!TryAcquireLogSlot("D", message))
        {
            return;
        }

        try { Log.Debug("{Message}", message); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
    }

    public static void LogException(Exception? ex, string? context = null)
    {
        if (ex == null)
        {
            return;
        }

        if (!TryAcquireLogSlot("X", (context ?? string.Empty) + "|" + ex.GetType().Name + "|" + ex.Message))
        {
            return;
        }

        try
        {
            if (!string.IsNullOrEmpty(context))
            {
                Log.Error(ex, "[{Context}] {Message}", context, ex.Message);
            }
            else
            {
                Log.Error(ex, "{Message}", ex.Message);
            }
        }
        catch (Exception erreurLog) when (erreurLog is not System.OutOfMemoryException) { _ = erreurLog; }
    }

    public static Task ShutdownAsync()
    {
        try { Log.CloseAndFlush(); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
        return Task.CompletedTask;
    }

    private static void PurgeLegacyLogs()
    {
        try
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-14);
            foreach (string file in Directory.EnumerateFiles(LogDir, "log_*.txt"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
    }
}
