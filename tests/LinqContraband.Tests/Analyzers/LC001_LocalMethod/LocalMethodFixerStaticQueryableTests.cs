using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer,
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC001_LocalMethod;

public partial class LocalMethodFixerTests
{
    [Fact]
    public async Task FixCrime_StaticQueryableWhere_SwitchesToEnumerable()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = Queryable.Where(db.Users, u => {|#0:IsAdult(u.Age)|});
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Enumerable.Where(db.Users.AsEnumerable(), u => IsAdult(u.Age));
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_StaticQueryableWhere_WithEnumerableShadow_UsesFullyQualifiedEnumerable()
    {
        var test = Usings + @"
class Enumerable
{
}

class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = Queryable.Where(db.Users, u => {|#0:IsAdult(u.Age)|});
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Enumerable
{
}

class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Enumerable.Where(db.Users.AsEnumerable(), u => IsAdult(u.Age));
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_StaticQueryableWhere_WithReorderedNamedSource_RewritesSourceArgument()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = Queryable.Where(
            predicate: u => {|#0:IsAdult(u.Age)|},
            source: db.Users);
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Enumerable.Where(
            predicate: u => IsAdult(u.Age),
            source: db.Users.AsEnumerable());
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_FullyQualifiedStaticQueryableWhere_PreservesQualifier()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Queryable.Where(db.Users, u => {|#0:IsAdult(u.Age)|});
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Enumerable.Where(db.Users.AsEnumerable(), u => IsAdult(u.Age));
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_ExtensionReceiverNamedQueryable_StaysExtensionCall()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var Queryable = db.Users;
        var query = Queryable.Where(u => {|#0:IsAdult(u.Age)|});
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var Queryable = db.Users;
        var query = Queryable.AsEnumerable().Where(u => IsAdult(u.Age));
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_AliasedStaticQueryableWhere_SwitchesToEnumerable()
    {
        var test = Usings + @"
using LinqQueryable = System.Linq.Queryable;

class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = LinqQueryable.Where(db.Users, u => {|#0:IsAdult(u.Age)|});
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
using LinqQueryable = System.Linq.Queryable;

class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Enumerable.Where(db.Users.AsEnumerable(), u => IsAdult(u.Age));
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_AliasedStaticQueryableWhere_WithEnumerableShadow_UsesFullyQualifiedEnumerable()
    {
        var test = Usings + @"
using LinqQueryable = System.Linq.Queryable;

class Enumerable
{
}

class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = LinqQueryable.Where(db.Users, u => {|#0:IsAdult(u.Age)|});
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
using LinqQueryable = System.Linq.Queryable;

class Enumerable
{
}

class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Enumerable.Where(db.Users.AsEnumerable(), u => IsAdult(u.Age));
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_NestedStaticQueryableChain_SwitchesContinuationToEnumerable()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = Queryable.OrderBy(
            Queryable.Where(db.Users, u => {|#0:IsAdult(u.Age)|}),
            u => u.Age);
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Enumerable.OrderBy(
            System.Linq.Enumerable.Where(db.Users.AsEnumerable(), u => IsAdult(u.Age)),
            u => u.Age);
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_StaticQueryableWhere_InsideAsQueryable_KeepsWrapperOnQueryable()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = Queryable.AsQueryable(
            Queryable.Where(db.Users, u => {|#0:IsAdult(u.Age)|}));
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = Queryable.AsQueryable(
            System.Linq.Enumerable.Where(db.Users.AsEnumerable(), u => IsAdult(u.Age)));
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_StaticQueryableWhere_WithUpstreamTake_KeepsTakeQueryable()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = Queryable.Where(
            Queryable.Take(db.Users, 10),
            u => {|#0:IsAdult(u.Age)|});
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Enumerable.Where(
            Queryable.Take(db.Users, 10).AsEnumerable(),
            u => IsAdult(u.Age));
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_StaticQueryableWhere_WithAwaitedSource_ParenthesizesSourceBoundary()
    {
        var test = Usings + @"
using System.Threading.Tasks;

class Program
{
    async Task Main()
    {
        var query = Queryable.Where(
            await GetUsersAsync(),
            u => {|#0:IsAdult(u.Age)|});
    }

    Task<IQueryable<User>> GetUsersAsync() => Task.FromResult(new DbContext().Users);
    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
using System.Threading.Tasks;

class Program
{
    async Task Main()
    {
        var query = System.Linq.Enumerable.Where(
            (await GetUsersAsync()).AsEnumerable(),
            u => IsAdult(u.Age));
    }

    Task<IQueryable<User>> GetUsersAsync() => Task.FromResult(new DbContext().Users);
    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_StaticQueryableJoin_WithReorderedNamedOuter_RewritesOuterArgument()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = Queryable.Join(
            inner: db.Roles,
            outerKeySelector: u => {|#0:UserKey(u.Age)|},
            outer: db.Users,
            innerKeySelector: r => r.UserAge,
            resultSelector: (u, r) => u);
    }

    int UserKey(int age) => age;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Enumerable.Join(
            inner: db.Roles,
            outerKeySelector: u => UserKey(u.Age),
            outer: db.Users.AsEnumerable(),
            innerKeySelector: r => r.UserAge,
            resultSelector: (u, r) => u);
    }

    int UserKey(int age) => age;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("UserKey"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_StaticQueryableThenBy_RewritesOrderedSourceChain()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = Queryable.ThenBy(
            Queryable.OrderBy(db.Users, u => u.Age),
            u => {|#0:SortKey(u.Age)|});
    }

    int SortKey(int age) => age;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Enumerable.ThenBy(
            System.Linq.Enumerable.OrderBy(db.Users.AsEnumerable(), u => u.Age),
            u => SortKey(u.Age));
    }

    int SortKey(int age) => age;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("SortKey"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_StaticQueryableThenBy_RewritesExtensionOrderedSourceChain()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = Queryable.ThenBy(
            db.Users.OrderBy(u => u.Age),
            u => {|#0:SortKey(u.Age)|});
    }

    int SortKey(int age) => age;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = System.Linq.Enumerable.ThenBy(
            db.Users.AsEnumerable().OrderBy(u => u.Age),
            u => SortKey(u.Age));
    }

    int SortKey(int age) => age;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("SortKey"));

        await testObj.RunAsync();
    }
}
