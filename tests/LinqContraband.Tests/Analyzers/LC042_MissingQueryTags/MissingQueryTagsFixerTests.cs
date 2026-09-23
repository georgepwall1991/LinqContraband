using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC042_MissingQueryTags.MissingQueryTagsAnalyzer,
    LinqContraband.Analyzers.LC042_MissingQueryTags.MissingQueryTagsFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC042_MissingQueryTags;

public class MissingQueryTagsFixerTests
{
    private const string TagWithKey = "MissingQueryTagsFixer.TagWith";
    private const string TagWithCallSiteKey = "MissingQueryTagsFixer.TagWithCallSite";

    private static string Wrap(string members) => MissingQueryTagsTests.EfCoreMock + @"
namespace TestApp
{
    using Microsoft.EntityFrameworkCore;

    public class Queries
    {
" + members + @"
    }
}";

    private static Task VerifyFixAsync(string before, string after, string key = TagWithKey)
    {
        return new CodeFixTest
        {
            TestCode = Wrap(before),
            FixedCode = Wrap(after),
            CodeActionEquivalenceKey = key
        }.RunAsync();
    }

    [Fact]
    public async Task TagWith_UsesContainingTypeAndMember()
    {
        await VerifyFixAsync(@"
        public object Run(AppDbContext db)
        {
            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();
        }", @"
        public object Run(AppDbContext db)
        {
            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).TagWith(""Queries.Run"").ToList();
        }");
    }

    [Fact]
    public async Task TagWithCallSite_IsOfferedWhenEfCoreHasIt()
    {
        await VerifyFixAsync(@"
        public object Run(AppDbContext db)
        {
            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();
        }", @"
        public object Run(AppDbContext db)
        {
            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).TagWithCallSite().ToList();
        }", TagWithCallSiteKey);
    }

    [Fact]
    public async Task TagWithCallSite_IsNotOfferedOnOlderEfCore()
    {
        var mock = MissingQueryTagsTests.EfCoreMock;
        var start = mock.IndexOf("        public static IQueryable<T> TagWithCallSite<T>", StringComparison.Ordinal);
        var end = mock.IndexOf('\n', start) + 1;
        var olderMock = mock.Remove(start, end - start);
        var code = Wrap(@"
        public object Run(AppDbContext db)
        {
            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();
        }").Replace(mock, olderMock);

        await new CodeFixTest
        {
            TestCode = code,
            FixedCode = code,
            CodeActionEquivalenceKey = TagWithCallSiteKey
        }.RunAsync();
    }

    [Fact]
    public async Task MultilineChain_TagGetsItsOwnLine()
    {
        await VerifyFixAsync(@"
        public object Run(AppDbContext db)
        {
            return db.Orders
                .Where(o => o.Total > 0)
                .OrderBy(o => o.Id)
                .Take(10)
                .{|LC042:ToList|}();
        }", @"
        public object Run(AppDbContext db)
        {
            return db.Orders
                .Where(o => o.Total > 0)
                .OrderBy(o => o.Id)
                .Take(10)
                .TagWith(""Queries.Run"")
                .ToList();
        }");
    }

    [Fact]
    public async Task AsyncTerminal()
    {
        await VerifyFixAsync(@"
        public async System.Threading.Tasks.Task<List<Order>> LoadAsync(AppDbContext db)
        {
            return await db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Skip(10).{|LC042:ToListAsync|}();
        }", @"
        public async System.Threading.Tasks.Task<List<Order>> LoadAsync(AppDbContext db)
        {
            return await db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Skip(10).TagWith(""Queries.LoadAsync"").ToListAsync();
        }");
    }

    [Fact]
    public async Task QuerySyntax_TagsTheParenthesizedQuery()
    {
        await VerifyFixAsync(@"
        public object Run(AppDbContext db)
        {
            return (from o in db.Orders
                    join c in db.Customers on o.CustomerId equals c.Id
                    where c.Name != null
                    select o.Id).{|LC042:ToList|}();
        }", @"
        public object Run(AppDbContext db)
        {
            return (from o in db.Orders
                    join c in db.Customers on o.CustomerId equals c.Id
                    where c.Name != null
                    select o.Id).TagWith(""Queries.Run"").ToList();
        }");
    }

    [Fact]
    public async Task LambdaAndLocalFunction_UseTheEnclosingMember()
    {
        await VerifyFixAsync(@"
        public object Run(AppDbContext db)
        {
            Func<object> load = () => db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();
            return Local();

            object Local() => db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(5).{|LC042:ToList|}();
        }", @"
        public object Run(AppDbContext db)
        {
            Func<object> load = () => db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).TagWith(""Queries.Run"").ToList();
            return Local();

            object Local() => db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(5).TagWith(""Queries.Run"").ToList();
        }");
    }

    [Fact]
    public async Task PropertyAndConstructor_UseTheirOwnNames()
    {
        await VerifyFixAsync(@"
        private readonly AppDbContext _db = new AppDbContext();
        private readonly object _warm;

        public Queries()
        {
            _warm = _db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();
        }

        public int OpenCount => _db.Orders.Where(o => o.Total > 0).Distinct().{|LC042:Count|}(o => o.CustomerId == 1);", @"
        private readonly AppDbContext _db = new AppDbContext();
        private readonly object _warm;

        public Queries()
        {
            _warm = _db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).TagWith(""Queries"").ToList();
        }

        public int OpenCount => _db.Orders.Where(o => o.Total > 0).Distinct().TagWith(""Queries.OpenCount"").Count(o => o.CustomerId == 1);");
    }

    [Fact]
    public async Task AddsEfCoreUsingWhenMissing()
    {
        var mock = MissingQueryTagsTests.EfCoreMock;
        const string app = @"
namespace Reports
{
    public class Report
    {
        public object Run(TestApp.AppDbContext db)
        {
            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();
        }
    }
}";
        const string fixedApp = @"
namespace Reports
{
    public class Report
    {
        public object Run(TestApp.AppDbContext db)
        {
            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).TagWith(""Report.Run"").ToList();
        }
    }
}";

        await new CodeFixTest
        {
            TestCode = mock + app,
            FixedCode = mock.Replace("using System.Threading.Tasks;\n", "using System.Threading.Tasks;\nusing Microsoft.EntityFrameworkCore;\n") + fixedApp,
            CodeActionEquivalenceKey = TagWithKey
        }.RunAsync();
    }

    [Fact]
    public async Task GlobalUsing_NoUsingAdded()
    {
        var mock = MissingQueryTagsTests.EfCoreMock;
        const string app = @"
namespace Reports
{
    public class Report
    {
        public object Run(TestApp.AppDbContext db)
        {
            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();
        }
    }
}";

        var test = new CodeFixTest
        {
            TestCode = mock + app,
            FixedCode = mock + app.Replace(".{|LC042:ToList|}()", @".TagWith(""Report.Run"").ToList()"),
            CodeActionEquivalenceKey = TagWithKey
        };
        test.TestState.Sources.Add(("/0/GlobalUsings.cs", "global using Microsoft.EntityFrameworkCore;\n"));
        test.FixedState.Sources.Add(("/0/GlobalUsings.cs", "global using Microsoft.EntityFrameworkCore;\n"));

        await test.RunAsync();
    }

    [Fact]
    public async Task StaticInvocationForm_HasNoFix()
    {
        var code = Wrap(@"
        public object Run(AppDbContext db)
        {
            return Enumerable.{|LC042:ToList|}(Queryable.Take(Queryable.OrderBy(Queryable.Where(db.Orders, o => o.Total > 0), o => o.Id), 10));
        }");

        await new CodeFixTest { TestCode = code, FixedCode = code }.RunAsync();
    }

    [Fact]
    public async Task FixAll_TagsEveryQuery()
    {
        await new CodeFixTest
        {
            TestCode = Wrap(@"
        public object Run(AppDbContext db)
        {
            var a = db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();
            var b = db.Customers.Include(c => c.Orders).Where(c => c.Name != null).OrderBy(c => c.Name).{|LC042:ToList|}();
            return a.Count + b.Count;
        }"),
            FixedCode = Wrap(@"
        public object Run(AppDbContext db)
        {
            var a = db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).TagWith(""Queries.Run"").ToList();
            var b = db.Customers.Include(c => c.Orders).Where(c => c.Name != null).OrderBy(c => c.Name).TagWith(""Queries.Run"").ToList();
            return a.Count + b.Count;
        }"),
            BatchFixedCode = Wrap(@"
        public object Run(AppDbContext db)
        {
            var a = db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).TagWith(""Queries.Run"").ToList();
            var b = db.Customers.Include(c => c.Orders).Where(c => c.Name != null).OrderBy(c => c.Name).TagWith(""Queries.Run"").ToList();
            return a.Count + b.Count;
        }"),
            CodeActionEquivalenceKey = TagWithKey
        }.RunAsync();
    }
}
