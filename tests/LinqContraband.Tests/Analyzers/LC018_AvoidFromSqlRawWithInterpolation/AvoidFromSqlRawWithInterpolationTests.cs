using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationFixer>;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

public class AvoidFromSqlRawWithInterpolationTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;

namespace Microsoft.EntityFrameworkCore
{
    public static class RelationalQueryableExtensions
    {
        public static IQueryable<TEntity> FromSqlRaw<TEntity>(this IQueryable<TEntity> source, string sql, params object[] parameters) => source;
        public static IQueryable<TEntity> FromSqlInterpolated<TEntity>(this IQueryable<TEntity> source, FormattableString sql) => source;
    }
}
";

    // Extra mock + sample types used only by the provider-variant tests below.
    // Kept separate from EFCoreMock to avoid shifting line numbers in the
    // existing fixer span assertions, and fully qualified because this string
    // is concatenated AFTER a top-level `using` directive (no mid-file
    // `using` clauses are allowed).
    private const string EFCoreDbContextMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : System.IDisposable
    {
        public void Dispose() { }
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
    }

    public class DbSet<TEntity> : System.Linq.IQueryable<TEntity> where TEntity : class
    {
        public System.Type ElementType => typeof(TEntity);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public System.Linq.IQueryProvider Provider => null;
        public System.Collections.Generic.IEnumerator<TEntity> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace LinqContraband.Test.Data
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    }
}
";

    // DatabaseFacade + the EF7+ raw-SQL facade extensions: SqlQueryRaw (raw string, unsafe with
    // interpolation) and its safe sibling SqlQuery (FormattableString). Kept separate so existing
    // span assertions are unaffected.
    private const string EFCoreFacadeMock = @"
namespace Microsoft.EntityFrameworkCore.Infrastructure
{
    public class DatabaseFacade { }
}
namespace Microsoft.EntityFrameworkCore
{
    public static class RelationalDatabaseFacadeExtensions
    {
        public static System.Linq.IQueryable<TResult> SqlQueryRaw<TResult>(this Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade databaseFacade, string sql, params object[] parameters) => null;
        public static System.Linq.IQueryable<TResult> SqlQuery<TResult>(this Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade databaseFacade, System.FormattableString sql) => null;
    }
}
";

    [Fact]
    public async Task FromSqlRaw_WithInterpolatedString_ShouldTriggerLC018()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:$""SELECT * FROM Table WHERE Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithConcatenatedString_ShouldTriggerLC018()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:""SELECT * FROM Table WHERE Id = "" + id|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithNestedConcatenatedString_ShouldTriggerLC018()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var name = ""A"";
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:""SELECT * FROM Table WHERE Name = "" + name + "" AND Id = "" + id|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithNamedSqlArgument_ShouldTriggerLC018()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw(parameters: new object[0], sql: {|LC018:$""SELECT * FROM Table WHERE Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithNoHoleInterpolatedString_ShouldNotTriggerLC018()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT * FROM Table"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithConstantOnlyInterpolation_ShouldNotTriggerLC018()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private const int Active = 1;

        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT * FROM Table WHERE Active = {Active}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithConstantLocalInterpolation_ShouldNotTriggerLC018()
    {
        // const locals are compile-time constants too, not just const fields;
        // both flows produce IOperation.ConstantValue.HasValue == true.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            const int Active = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT * FROM Table WHERE Active = {Active}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithNumericLiteralInterpolation_ShouldNotTriggerLC018()
    {
        // A bare numeric literal in an interpolation hole is always a
        // compile-time constant and cannot embed runtime data.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT * FROM Table WHERE Id = {1}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithMultipleConstantHoles_ShouldNotTriggerLC018()
    {
        // Two holes, both compile-time constants: HasNonConstantInterpolation
        // must require *every* hole to be constant for the safe-shape gate to
        // hold.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private const int Active = 1;
        private const int Limit = 100;

        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT TOP {Limit} * FROM Table WHERE Active = {Active}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithNameofInterpolation_ShouldNotTriggerLC018()
    {
        // `nameof(...)` is a compile-time constant string; embedding it into
        // a raw SQL fragment is identifier-only and cannot smuggle user data.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT {nameof(System.Int32.MaxValue)} FROM Table"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithMixedConstantAndRuntimeHoles_ShouldTriggerLC018()
    {
        // Boundary lock-in: a single non-constant hole alongside a constant
        // hole must still trigger LC018. The constant hole does not launder
        // the runtime-data hole next to it.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private const int Active = 1;

        public void TestMethod(int id)
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:$""SELECT * FROM Table WHERE Active = {Active} AND Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithStaticReadonlyFieldInterpolation_ShouldTriggerLC018()
    {
        // Boundary lock-in: `static readonly` is not a compile-time constant —
        // the field's runtime value is observable and can be reassigned via
        // reflection or static initializers. LC018 must treat it as
        // potentially unsafe even though the typical value is fixed.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private static readonly int Active = 1;

        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:$""SELECT * FROM Table WHERE Active = {Active}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithConstantString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw(""SELECT * FROM Table"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithParameterizedString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw(""SELECT * FROM Table WHERE Id = {0}"", id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithInterpolatedAlias_ShouldNotTriggerLC018()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var sql = $""SELECT * FROM Table WHERE Id = {id}"";
            var result = query.FromSqlRaw(sql);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlInterpolated_WithInterpolatedString_ShouldNotTriggerLC018()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlInterpolated($""SELECT * FROM Table WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithConcatenatedAlias_ShouldNotTriggerLC018()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var sql = ""SELECT * FROM Table WHERE Id = "" + id;
            var result = query.FromSqlRaw(sql);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_InLookalikeNamespace_ShouldNotTriggerLC018()
    {
        var test = @"
using System;
using System.Linq;
using Microsoft.EntityFrameworkCoreFake;

namespace Microsoft.EntityFrameworkCoreFake
{
    public static class RelationalQueryableExtensions
    {
        public static IQueryable<TEntity> FromSqlRaw<TEntity>(this IQueryable<TEntity> source, string sql, params object[] parameters) => source;
    }
}

namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT * FROM Table WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_OnNonQueryableEfNamespaceHelper_ShouldNotTriggerLC018()
    {
        var test = @"
using System;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class CustomRawExtensions
    {
        public static string FromSqlRaw(this string source, string sql, params object[] parameters) => source;
    }
}

namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(int id)
        {
            var result = ""not a query"".FromSqlRaw($""SELECT * FROM Table WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceFromSqlRawWithFromSqlInterpolated()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT * FROM Table WHERE Id = {id}"");
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlInterpolated($""SELECT * FROM Table WHERE Id = {id}"");
        }
    }
}";

        var expected = VerifyFix.Diagnostic("LC018").WithSpan(22, 43, 22, 81).WithArguments("FromSqlInterpolated", "FromSqlRaw");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceNamedFromSqlRawWithFromSqlInterpolated()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw(sql: {|LC018:$""SELECT * FROM Table WHERE Id = {id}""|});
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlInterpolated(sql: $""SELECT * FROM Table WHERE Id = {id}"");
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenRawParametersArePresent()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw(parameters: new object[0], sql: {|LC018:$""SELECT * FROM Table WHERE Id = {id}""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenInterpolationIsInsideSqlStringLiteral()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var name = ""admin"";
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:$""SELECT * FROM Table WHERE Name = '{name}'""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_ForConcatenation()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:""SELECT * FROM Table WHERE Id = "" + id|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task FromSqlRaw_OnDbSet_WithUnsafeInterpolation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = db.Users.FromSqlRaw({|LC018:$""SELECT * FROM Users WHERE Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_OnDbContextSetT_WithUnsafeInterpolation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = db.Set<User>().FromSqlRaw({|LC018:$""SELECT * FROM Users WHERE Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_AsStaticExtensionCall_WithUnsafeInterpolation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = RelationalQueryableExtensions.FromSqlRaw(db.Users, {|LC018:$""SELECT * FROM Users WHERE Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlInterpolated_OnDbSet_ShouldNotTrigger()
    {
        // Negative guardrail: the safe sibling API must stay quiet on DbSet
        // receivers too, not just IQueryable wrappers, so the rule's
        // "provider variant" coverage isn't lopsided.
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = db.Users.FromSqlInterpolated($""SELECT * FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlInterpolated_OnDbContextSetT_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = db.Set<User>().FromSqlInterpolated($""SELECT * FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlInterpolated_AsStaticExtensionCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = RelationalQueryableExtensions.FromSqlInterpolated(db.Users, $""SELECT * FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQueryRaw_WithInterpolatedString_ShouldTriggerLC018()
    {
        // SqlQueryRaw<T> on the DatabaseFacade takes a raw string; an interpolated string with a
        // runtime hole is an injection sink, exactly like FromSqlRaw.
        var test = @"using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;" + EFCoreFacadeMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void Run(DatabaseFacade db, int id)
        {
            var rows = db.SqlQueryRaw<int>({|LC018:$""SELECT Id FROM Users WHERE Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQueryRaw_WithConcatenation_ShouldTriggerLC018()
    {
        var test = @"using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;" + EFCoreFacadeMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void Run(DatabaseFacade db, string name)
        {
            var rows = db.SqlQueryRaw<int>({|LC018:""SELECT Id FROM Users WHERE Name = '"" + name + ""'""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQuery_WithFormattableString_ShouldNotTrigger()
    {
        // SqlQuery<T> takes a FormattableString and parameterizes the holes, so it is the safe
        // sibling and must stay quiet.
        var test = @"using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;" + EFCoreFacadeMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void Run(DatabaseFacade db, int id)
        {
            var rows = db.SqlQuery<int>($""SELECT Id FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQueryRaw_WithConstantInterpolation_ShouldNotTrigger()
    {
        // Constant-only interpolation has no runtime hole, so it is not an injection vector.
        var test = @"using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;" + EFCoreFacadeMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private const int Max = 10;
        public void Run(DatabaseFacade db)
        {
            var rows = db.SqlQueryRaw<int>($""SELECT TOP {Max} Id FROM Users"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FixAll_RewritesAllFromSqlRawWithInterpolatedStringInstances()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id1 = 1;
            var id2 = 2;
            var query = new int[0].AsQueryable();
            var result1 = query.FromSqlRaw({|#0:$""SELECT * FROM Table WHERE Id = {id1}""|});
            var result2 = query.FromSqlRaw({|#1:$""SELECT * FROM Table WHERE Id = {id2}""|});
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id1 = 1;
            var id2 = 2;
            var query = new int[0].AsQueryable();
            var result1 = query.FromSqlInterpolated($""SELECT * FROM Table WHERE Id = {id1}"");
            var result2 = query.FromSqlInterpolated($""SELECT * FROM Table WHERE Id = {id2}"");
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "ReplaceFromSqlRawWithInterpolated"
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC018", DiagnosticSeverity.Warning)
                .WithLocation(0));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC018", DiagnosticSeverity.Warning)
                .WithLocation(1));

        await testObj.RunAsync();
    }
}
