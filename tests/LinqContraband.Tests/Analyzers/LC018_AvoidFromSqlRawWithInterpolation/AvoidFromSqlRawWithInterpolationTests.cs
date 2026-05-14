using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationFixer>;

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

        var expected = VerifyFix.Diagnostic("LC018").WithSpan(22, 43, 22, 81);
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
}
