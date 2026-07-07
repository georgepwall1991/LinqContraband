using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

public partial class NPlusOneLooperTests
{
    [Fact]
    public async Task InMemoryAsQueryable_IsIgnored()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var users = new List<User>().AsQueryable();

        foreach (var id in new[] { 1, 2, 3 })
        {
            var count = users.Count(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InMemoryAsEnumerableAggregate_IsIgnored()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var users = new List<User>();

        foreach (var id in new[] { 1, 2, 3 })
        {
            var count = users.AsEnumerable().Count(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IQueryableParameter_IsIgnoredAsAmbiguous()
    {
        var test = Usings + @"
class Program
{
    void Main(IQueryable<User> query)
    {
        foreach (var id in new[] { 1, 2, 3 })
        {
            var count = query.Count(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IQueryableProperty_IsIgnoredAsAmbiguous()
    {
        var test = Usings + @"
class Program
{
    private readonly MyDbContext _db = new MyDbContext();

    void Main()
    {
        foreach (var id in new[] { 1, 2, 3 })
        {
            var count = _db.AmbiguousUsers.Count(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultiAssignedLocal_IsIgnoredAsAmbiguous()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        IQueryable<User> query = db.Users;
        query = db.Users.Where(u => u.Id > 10);

        foreach (var id in new[] { 1, 2, 3 })
        {
            var count = query.Count(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QueryConstructionOnly_IsIgnored()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var id in new[] { 1, 2, 3 })
        {
            var query = db.Users.Where(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MaterializedListAggregates_InLoop_AreIgnored()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        var users = db.Users.ToList();

        foreach (var id in new[] { 1, 2, 3 })
        {
            var single = users.Single(u => u.Id == id);
            var sum = users.Sum(u => u.Id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AccessorsWithoutExecution_AreIgnored()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var user in db.Users.ToList())
        {
            db.Entry(user).Reference(u => u.Profile);
            db.Entry(user).Collection(u => u.Orders);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InvocationInsideLambdaDeclaredInLoop_IsIgnored()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new MyDbContext();
        foreach (var id in new[] { 1, 2, 3 })
        {
            Func<int> countUsers = () => db.Users.Count(u => u.Id == id);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
