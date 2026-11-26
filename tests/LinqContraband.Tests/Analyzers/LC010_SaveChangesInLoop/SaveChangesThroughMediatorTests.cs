using Microsoft.CodeAnalysis.Testing;
using LinqContraband.Analyzers.LC010_SaveChangesInLoop;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// A command sent through a mediator once per item (<c>mediator.Send(new RecordAudit(...))</c>) saves once per item
/// when its handler saves. LC010 follows <c>Send</c>, <c>SendAsync</c> and <c>InvokeAsync</c> to the handlers this
/// project declares for the request's type and reports the call when every one of them saves.
/// </summary>
public class SaveChangesThroughMediatorTests
{
    private static string Program(string body, string extraTypes = "") => @"
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Program
{
    async Task Run(List<int> ids, IMediator mediator)
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

public sealed class Describe : IRequest<string>
{
    public Describe(int id) => Id = id;
    public int Id { get; }
}

public sealed class DescribeHandler : IRequestHandler<Describe, string>
{
    public Task<string> Handle(Describe request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Id.ToString());
}
" + extraTypes;

    private static DiagnosticResult Helper(int location, string helper, string save) =>
        VerifyCS.Diagnostic(SaveChangesInLoopAnalyzer.HelperCallRule).WithLocation(location).WithArguments(helper, save);

    [Fact]
    public Task CommandWhoseHandlerSaves_SentPerItem_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"foreach (var id in ids)
        {
            await {|#0:mediator.Send(new RecordAudit(""customer "" + id))|};
        }"),
            Helper(0, "Send", "SaveChangesAsync"));

    [Fact]
    public Task CommandWhoseHandlerSaves_SentInForEachLambda_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"ids.ForEach(id => {|#0:mediator.Send(new RecordAudit(""customer "" + id))|});"),
            Helper(0, "Send", "SaveChangesAsync"));

    [Fact]
    public Task CommandSentOnce_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"await mediator.Send(new RecordAudit(""batch of "" + ids.Count));"));

    [Fact]
    public Task RequestWhoseHandlerDoesNotSave_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"foreach (var id in ids)
        {
            var text = await mediator.Send(new Describe(id));
        }"));

    [Fact]
    public Task CommandWithASecondHandlerThatDoesNotSave_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"foreach (var id in ids)
        {
            await mediator.Send(new RecordAudit(""customer "" + id));
        }", @"
public sealed class ConsoleAuditHandler : IRequestHandler<RecordAudit, int>
{
    public Task<int> Handle(RecordAudit request, CancellationToken cancellationToken)
    {
        Console.WriteLine(request.Message);
        return Task.FromResult(0);
    }
}
"));
}
