using System.Runtime.InteropServices;

namespace Coclico.Modules.UpdateCenter.Services;

internal class RestorePointService
{
    public const int DriverUpdateType = 12;

    public virtual bool Create(string description)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Length > 64)
            throw new ArgumentException("Description must be between 1 and 64 characters.");

        var state = new RestoreState
        {
            dwEventType = 100,
            dwRestorePointType = DriverUpdateType,
            llSequenceNumber = 0,
            szDescription = description
        };

        bool succeeded = SRSetRestorePointW(ref state, out SmgrStatus status);
        return succeeded && status.nStatus == 0 && status.llSequenceNumber != 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestoreState
    {
        public int dwEventType;
        public int dwRestorePointType;
        public long llSequenceNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SmgrStatus
    {
        public int nStatus;
        public long llSequenceNumber;
    }

    [DllImport("SrClient.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SRSetRestorePointW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SRSetRestorePointW(ref RestoreState restorePoint, out SmgrStatus status);
}
