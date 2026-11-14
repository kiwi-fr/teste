using System.Diagnostics;

namespace Coclico.Services;

internal static class ProcessSnapshotProvider
{
    private const long SnapshotLifetimeMs = 2000;
    private static readonly Lock Gate = new();
    private static Process[] _snapshot = [];
    private static long _stamp;

    public static Process[] Capture()
    {
        lock (Gate)
        {
            long now = Environment.TickCount64;
            if (_snapshot.Length == 0 || now - _stamp >= SnapshotLifetimeMs)
            {
                _snapshot = Process.GetProcesses();
                _stamp = now;
            }

            return _snapshot;
        }
    }
}
