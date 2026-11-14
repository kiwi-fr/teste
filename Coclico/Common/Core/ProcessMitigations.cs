using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Diagnostics;

namespace Coclico.Services;

[SupportedOSPlatform("windows")]
internal static class ProcessMitigations
{
    private const int ProcessMitigationDepPolicy = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct DepPolicy
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.U1)] public bool Permanent;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetProcessMitigationPolicy(int policy, ref DepPolicy policyData, int policyDataSize);

    private const uint DepEnable = 0x1;
    private const uint DepDisableAtlThunks = 0x2;

    public static bool TryEnableDataExecutionPrevention()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            DepPolicy policy = new()
            {
                Flags = DepEnable | DepDisableAtlThunks,
                Permanent = false
            };

            if (SetProcessMitigationPolicy(ProcessMitigationDepPolicy, ref policy, Marshal.SizeOf<DepPolicy>()))
            {
                return true;
            }

            LoggingService.LogWarning($"SetProcessMitigationPolicy(DEP) failed with error {Marshal.GetLastWin32Error()}.");
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LoggingService.LogException(ex, "ProcessMitigations.TryEnableDataExecutionPrevention");
            return false;
        }
    }
}
