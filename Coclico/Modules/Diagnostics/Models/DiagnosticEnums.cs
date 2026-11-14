namespace Coclico.Modules.Diagnostics.Models;

internal enum DiagnosticMode
{
    Quick,
    Full,
    Targeted
}

internal enum DiagnosticRunStatus
{
    Running,
    Completed,
    Partial,
    Cancelled,
    Failed
}

public enum DiagnosticSeverity
{
    Success = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

internal enum DiagnosticConfidence
{
    Low = 0,
    Medium = 1,
    High = 2
}

internal enum DiagnosticCheckStatus
{
    Passed,
    Findings,
    Skipped,
    Unsupported,
    TimedOut,
    Failed,
    Cancelled
}

internal enum DiagnosticCategory
{
    Windows,
    Cpu,
    Gpu,
    Memory,
    Storage,
    Network,
    Power,
    Devices,
    Processes,
    Security,
    Motherboard
}

internal enum DiagnosticValueState
{
    Measured,

    NotExposed,

    AccessDenied,

    SourceError,

    Stale,

    NotApplicable
}
