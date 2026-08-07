using System;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

/// <summary>
/// The slice of analysis context LC045 needs, decoupled from how the analyzer is registered.
/// LC045 reports on a navigation access that sits outside the span of the invocation or loop
/// that triggered analysis, so it must be registered per operation block: an operation-scoped
/// action makes Roslyn classify the report as a non-local (compilation-level) diagnostic,
/// which suppresses live IDE analysis and makes the code fix unreliable.
/// </summary>
internal readonly struct MissingIncludeAnalysisContext
{
    private readonly Action<Diagnostic> _reportDiagnostic;

    internal MissingIncludeAnalysisContext(
        IOperation operation,
        Compilation compilation,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken
    )
    {
        Operation = operation;
        Compilation = compilation;
        _reportDiagnostic = reportDiagnostic;
        CancellationToken = cancellationToken;
    }

    internal IOperation Operation { get; }

    internal Compilation Compilation { get; }

    internal CancellationToken CancellationToken { get; }

    internal void ReportDiagnostic(Diagnostic diagnostic) => _reportDiagnostic(diagnostic);
}
