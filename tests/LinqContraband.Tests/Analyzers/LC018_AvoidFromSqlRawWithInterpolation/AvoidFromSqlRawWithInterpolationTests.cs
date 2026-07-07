using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

public partial class AvoidFromSqlRawWithInterpolationTests
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

}
