using System.Text;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Leftover mediator-dispatch arms the shipped
/// <c>Send</c>/<c>SendAsync</c>-with-a-source-body fixtures do not isolate.
/// Dropping <c>InvokeAsync</c> fails only <see cref="LeftoverInvokeAsync_Reports"/>.
/// Dropping <c>SendAsync</c> fails <see cref="LeftoverSendAsync_OnInterface_Reports"/>
/// and the shipped hand-written mediator whose <c>SendAsync</c> body casts to
/// <c>IRequestHandler&lt;TRequest, TResponse&gt;</c> (that compilation also
/// declares a non-querying handler, so helper-following stays quiet). A struct
/// request, a <c>*Handler*</c> interface that is not <c>IRequestHandler</c>, and
/// an <c>Execute</c> method on that interface each fail only their pin. Nine
/// handlers stay quiet so dropping the mediator <c>MaxImplementations</c> cap is
/// visible; the interface-dispatch nine-implementer fixture stays green. A
/// lookalike <c>InvokeAsync</c> whose request has only an
/// <c>IProcessor&lt;T&gt;</c> stays quiet.
/// </summary>
public partial class NPlusOneLooperTests
{
    private const string LeftoverMediatorBuses = @"
public interface IMessageBus
{
    Task<TResponse> InvokeAsync<TResponse>(object message, System.Threading.CancellationToken cancellationToken = default);
}

public interface ISender
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, System.Threading.CancellationToken cancellationToken = default);
}
";

    private static string LeftoverMediatorProgram(string members, string extraTypes = "") =>
        MediatorProgram(members, LeftoverMediatorBuses + extraTypes);

    [Fact]
    public Task LeftoverInvokeAsync_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            LeftoverMediatorProgram(@"
    async Task Run(int[] ids, IMessageBus bus)
    {
        foreach (var id in ids)
        {
            var user = await {|#0:bus.InvokeAsync<User>(new GetUser(id))|};
        }
    }"),
            HelperCall(0, "InvokeAsync", "FindAsync"));

    [Fact]
    public Task LeftoverSendAsync_OnInterface_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            LeftoverMediatorProgram(@"
    async Task Run(int[] ids, ISender sender)
    {
        foreach (var id in ids)
        {
            var user = await {|#0:sender.SendAsync(new GetUser(id))|};
        }
    }"),
            HelperCall(0, "SendAsync", "FindAsync"));

    [Fact]
    public Task LeftoverStructRequest_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            LeftoverMediatorProgram(@"
    async Task Run(int[] ids, IMediator mediator)
    {
        foreach (var id in ids)
        {
            var user = await {|#0:mediator.Send(new GetFlag(id))|};
        }
    }", @"
public struct GetFlag : IRequest<User> { public GetFlag(int id) => Id = id; public int Id { get; } }

public sealed class GetFlagHandler : IRequestHandler<GetFlag, User>
{
    private readonly MyDbContext _db = new MyDbContext();
    public Task<User> Handle(GetFlag request, System.Threading.CancellationToken cancellationToken) =>
        _db.Users.FindAsync(request.Id);
}
"),
            HelperCall(0, "Send", "FindAsync"));

    [Fact]
    public Task LeftoverMessageHandlerInterface_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            LeftoverMediatorProgram(@"
    async Task Run(int[] ids, IMediator mediator)
    {
        foreach (var id in ids)
        {
            var user = await {|#0:mediator.Send(new GetLabel(id))|};
        }
    }", @"
public interface IMessageHandler<TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, System.Threading.CancellationToken cancellationToken);
}

public sealed class GetLabel : IRequest<User> { public GetLabel(int id) => Id = id; public int Id { get; } }

public sealed class GetLabelHandler : IMessageHandler<GetLabel, User>
{
    private readonly MyDbContext _db = new MyDbContext();
    public Task<User> Handle(GetLabel request, System.Threading.CancellationToken cancellationToken) =>
        _db.Users.FindAsync(request.Id);
}
"),
            HelperCall(0, "Send", "FindAsync"));

    [Fact]
    public Task LeftoverExecuteOnCommandHandler_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            LeftoverMediatorProgram(@"
    async Task Run(int[] ids, IMediator mediator)
    {
        foreach (var id in ids)
        {
            var user = await {|#0:mediator.Send(new GetProfile(id))|};
        }
    }", @"
public interface ICommandHandler<TRequest, TResponse>
{
    Task<TResponse> Execute(TRequest request, System.Threading.CancellationToken cancellationToken);
}

public sealed class GetProfile : IRequest<User> { public GetProfile(int id) => Id = id; public int Id { get; } }

public sealed class GetProfileHandler : ICommandHandler<GetProfile, User>
{
    private readonly MyDbContext _db = new MyDbContext();
    public Task<User> Execute(GetProfile request, System.Threading.CancellationToken cancellationToken) =>
        _db.Users.FindAsync(request.Id);
}
"),
            HelperCall(0, "Send", "FindAsync"));

    [Fact]
    public Task NineHandlers_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            LeftoverMediatorProgram(@"
    async Task Run(int[] ids, IMediator mediator)
    {
        foreach (var id in ids)
        {
            var user = await mediator.Send(new Crowd(id));
        }
    }", NineCrowdHandlers()));

    [Fact]
    public Task LookalikeInvokeAsync_ProcessorInterface_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            LeftoverMediatorProgram(@"
    async Task Run(int[] ids, IInvoker invoker)
    {
        foreach (var id in ids)
        {
            await invoker.InvokeAsync(new Mail(id));
        }
    }", @"
public sealed class Mail { public Mail(int id) => Id = id; public int Id { get; } }

public interface IInvoker
{
    Task InvokeAsync(Mail mail);
}

public sealed class MailProcessor : IProcessor<Mail>
{
    private readonly MyDbContext _db = new MyDbContext();
    public Task Process(Mail mail) => _db.Users.ToListAsync();
}

public interface IProcessor<T>
{
    Task Process(T item);
}
"));

    private static string NineCrowdHandlers()
    {
        var text = new StringBuilder();
        text.AppendLine("public sealed class Crowd : IRequest<User> { public Crowd(int id) => Id = id; public int Id { get; } }");
        for (var i = 1; i <= 9; i++)
        {
            text.AppendLine($@"
public sealed class CrowdHandler{i} : IRequestHandler<Crowd, User>
{{
    private readonly MyDbContext _db = new MyDbContext();
    public Task<User> Handle(Crowd request, System.Threading.CancellationToken cancellationToken) =>
        _db.Users.FindAsync(request.Id);
}}");
        }

        return text.ToString();
    }
}
