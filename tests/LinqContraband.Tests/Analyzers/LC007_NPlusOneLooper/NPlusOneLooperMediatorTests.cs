using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A mediator call in a loop (<c>mediator.Send(new GetUser(id))</c>) runs the request's handler once per item.
/// LC007 follows <c>Send</c>, <c>SendAsync</c> and <c>InvokeAsync</c> to the handlers this project declares for the
/// request's type (a type implementing a generic <c>*Handler*</c> interface whose first type argument is the
/// request) and reports the call when every one of them runs a query. The mediator's own body, which only looks
/// the handler up, is not what runs per item.
/// </summary>
public partial class NPlusOneLooperTests
{
    private const string MediatorTypes = @"
public interface IRequest<TResponse> { }

public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, System.Threading.CancellationToken cancellationToken);
}

public interface IMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, System.Threading.CancellationToken cancellationToken = default);
}

public sealed class GetUser : IRequest<User> { public GetUser(int id) => Id = id; public int Id { get; } }

public sealed class GetUserHandler : IRequestHandler<GetUser, User>
{
    private readonly MyDbContext _db = new MyDbContext();
    public Task<User> Handle(GetUser request, System.Threading.CancellationToken cancellationToken) =>
        _db.Users.FindAsync(request.Id);
}
";

    private static string MediatorProgram(string members, string extraTypes = "") => HelperProgram(members) + MediatorTypes + extraTypes;

    [Fact]
    public Task Mediator_SendInForeach_ReportsTheHandlersQuery() =>
        VerifyCS.VerifyAnalyzerAsync(
            MediatorProgram(@"
    async Task Run(int[] ids, IMediator mediator)
    {
        foreach (var id in ids)
        {
            var user = await {|#0:mediator.Send(new GetUser(id))|};
        }
    }"),
            HelperCall(0, "Send", "FindAsync"));

    [Fact]
    public Task Mediator_SendInSelectLambda_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            MediatorProgram(@"
    void Run(int[] ids, IMediator mediator)
    {
        var users = ids.Select(id => {|#0:mediator.Send(new GetUser(id))|}).ToList();
    }"),
            PerElementLambdaHelperCall(0, "Send", "FindAsync", "Select"));

    [Fact]
    public Task Mediator_RequestHeldInALocal_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            MediatorProgram(@"
    async Task Run(int[] ids, IMediator mediator)
    {
        foreach (var id in ids)
        {
            var request = new GetUser(id);
            var user = await {|#0:mediator.Send(request, default)|};
        }
    }"),
            HelperCall(0, "Send", "FindAsync"));

    [Fact]
    public Task Mediator_HandWrittenMediatorWithASourceBody_FollowsTheHandler() =>
        VerifyCS.VerifyAnalyzerAsync(
            MediatorProgram(@"
    async Task Run(int[] ids, LocalMediator mediator)
    {
        foreach (var id in ids)
        {
            var user = await {|#0:mediator.SendAsync<GetUser, User>(new GetUser(id))|};
        }
    }", @"
public sealed class GetNoteHandler : IRequestHandler<GetNote, string>
{
    public Task<string> Handle(GetNote request, System.Threading.CancellationToken cancellationToken) => Task.FromResult(""note"");
}

public sealed class GetNote : IRequest<string> { public GetNote(int id) => Id = id; public int Id { get; } }

public sealed class LocalMediator
{
    private readonly Dictionary<Type, object> _handlers = new Dictionary<Type, object>();

    public Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request) where TRequest : IRequest<TResponse> =>
        ((IRequestHandler<TRequest, TResponse>)_handlers[typeof(TRequest)]).Handle(request, default);
}
"),
            HelperCall(0, "SendAsync", "FindAsync"));

    [Fact]
    public Task Mediator_SendOutsideALoop_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            MediatorProgram(@"
    async Task Run(int id, IMediator mediator)
    {
        var user = await mediator.Send(new GetUser(id));
    }"));

    [Fact]
    public Task Mediator_HandlerThatDoesNotQuery_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            MediatorProgram(@"
    async Task Run(int[] ids, IMediator mediator)
    {
        foreach (var id in ids)
        {
            var label = await mediator.Send(new FormatId(id));
        }
    }", @"
public sealed class FormatId : IRequest<string> { public FormatId(int id) => Id = id; public int Id { get; } }

public sealed class FormatIdHandler : IRequestHandler<FormatId, string>
{
    public Task<string> Handle(FormatId request, System.Threading.CancellationToken cancellationToken) =>
        Task.FromResult(request.Id.ToString());
}
"));

    [Fact]
    public Task Mediator_TwoHandlersWhereOneIsInMemory_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            MediatorProgram(@"
    async Task Run(int[] ids, IMediator mediator)
    {
        foreach (var id in ids)
        {
            var user = await mediator.Send(new GetUser(id));
        }
    }", @"
public sealed class CachedGetUserHandler : IRequestHandler<GetUser, User>
{
    public Task<User> Handle(GetUser request, System.Threading.CancellationToken cancellationToken) =>
        Task.FromResult(new User());
}
"));

    [Fact]
    public Task Mediator_BatchRequestSentOnce_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            MediatorProgram(@"
    async Task Run(int[] ids, IMediator mediator)
    {
        var users = await mediator.Send(new GetUsers(ids));
        foreach (var user in users)
        {
            Console.WriteLine(user.Id);
        }
    }", @"
public sealed class GetUsers : IRequest<List<User>> { public GetUsers(int[] ids) => Ids = ids; public int[] Ids { get; } }

public sealed class GetUsersHandler : IRequestHandler<GetUsers, List<User>>
{
    private readonly MyDbContext _db = new MyDbContext();
    public Task<List<User>> Handle(GetUsers request, System.Threading.CancellationToken cancellationToken) =>
        _db.Users.Where(u => request.Ids.Contains(u.Id)).ToListAsync();
}
"));

    [Fact]
    public Task Mediator_PublishAndOtherNames_AreNotFollowed() =>
        VerifyCS.VerifyAnalyzerAsync(
            MediatorProgram(@"
    async Task Run(int[] ids, IBus bus)
    {
        foreach (var id in ids)
        {
            await bus.Publish(new GetUser(id));
            await bus.Dispatch(new GetUser(id));
        }
    }", @"
public interface IBus
{
    Task Publish(object message);
    Task Dispatch(object message);
}
"));

    [Fact]
    public Task Mediator_RequestTypeWithoutAHandlerInterface_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            MediatorProgram(@"
    async Task Run(int[] ids, IMailer mailer)
    {
        foreach (var id in ids)
        {
            await mailer.Send(new Mail(id));
        }
    }", @"
public sealed class Mail { public Mail(int id) => Id = id; public int Id { get; } }

public interface IMailer
{
    Task Send(Mail mail);
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

    [Fact]
    public Task Mediator_RequestTypedAsTheInterface_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            MediatorProgram(@"
    async Task Run(IEnumerable<IRequest<User>> requests, IMediator mediator)
    {
        foreach (var request in requests)
        {
            var user = await mediator.Send(request);
        }
    }"));
}
