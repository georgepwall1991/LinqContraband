using LinqContraband.Analyzers.LC010_SaveChangesInLoop;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// Leftover mediator names the shipped LC010 <c>Send</c> fixtures do not isolate.
/// Dropping <c>InvokeAsync</c> fails only <see cref="LeftoverInvokeAsync_Reports"/>.
/// Dropping <c>SendAsync</c> fails only <see cref="LeftoverSendAsync_OnInterface_Reports"/>.
/// Does not restage <c>SaveChangesThroughMediatorTests.cs</c>.
/// </summary>
public class SaveChangesThroughMediatorLeftoverCoverageTests
{
    private static string Program(string body, string extraTypes = "") => @"
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Program
{
    async Task Run(List<int> ids, IMediator mediator, IMessageBus bus, ISender sender)
    {
        " + body + @"
    }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public void Add(object entity) { }
        public void Dispose() { }
    }
}

public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public interface IRequest<TResponse> { }

public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface IMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

public interface IMessageBus
{
    Task<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default);
}

public interface ISender
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

public sealed class RecordAudit : IRequest<int>
{
    public RecordAudit(string message) => Message = message;
    public string Message { get; }
}

public sealed class RecordAuditHandler : IRequestHandler<RecordAudit, int>
{
    private readonly AppDbContext _db = new AppDbContext();

    public async Task<int> Handle(RecordAudit request, CancellationToken cancellationToken)
    {
        _db.Add(request.Message);
        return await _db.SaveChangesAsync(cancellationToken);
    }
}
" + extraTypes;

    private static DiagnosticResult Helper(int location, string helper, string save) =>
        VerifyCS.Diagnostic(SaveChangesInLoopAnalyzer.HelperCallRule).WithLocation(location).WithArguments(helper, save);

    [Fact]
    public Task LeftoverInvokeAsync_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"foreach (var id in ids)
        {
            await {|#0:bus.InvokeAsync<int>(new RecordAudit(""customer "" + id))|};
        }"),
            Helper(0, "InvokeAsync", "SaveChangesAsync"));

    [Fact]
    public Task LeftoverSendAsync_OnInterface_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"foreach (var id in ids)
        {
            await {|#0:sender.SendAsync(new RecordAudit(""customer "" + id))|};
        }"),
            Helper(0, "SendAsync", "SaveChangesAsync"));
}
