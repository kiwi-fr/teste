namespace Coclico.Services;

internal enum PriorityLevel
{
    Idle = 0,
    BelowNormal = 1,
    Normal = 2,
    AboveNormal = 3,
    High = 4,
    RealTime = 5,
}

internal sealed record QosResult(bool Success, string Message, int ProcessId, string ProcessName);

internal interface IResourceAllocator
{
    QosResult SetProcessPriority(int processId, PriorityLevel level);

    QosResult SetProcessorAffinity(int processId, long affinityMask);

    QosResult TrimProcessWorkingSet(int processId);

    QosResult ApplyProfile(int processId, PriorityLevel level, long? affinityMask = null);

    PriorityLevel? GetProcessPriority(int processId);

    IReadOnlyList<ProcessQosSnapshot> GetSystemSnapshot();
}

internal sealed record ProcessQosSnapshot(
    int Pid,
    string Name,
    PriorityLevel Priority,
    long AffinityMask,
    long WorkingSetMb);
