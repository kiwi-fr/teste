namespace Coclico.Modules.Diagnostics.Services;

using Coclico.Modules.Diagnostics.Models;

internal interface IDiagnosticCheck
{
    string Id { get; }

    DiagnosticCategory Category { get; }

    bool IsQuick { get; }

    bool IsSupported(DiagnosticEnvironment environment);

    Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context);
}
