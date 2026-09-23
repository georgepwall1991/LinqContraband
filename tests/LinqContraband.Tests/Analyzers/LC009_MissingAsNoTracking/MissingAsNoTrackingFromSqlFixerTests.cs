using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer,
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer,
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingFixer>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

// FromSqlRaw, FromSql, FromSqlInterpolated and the SQL Server temporal operators are declared
// on DbSet<T>, not IQueryable<T>. AsNoTracking() returns IQueryable<T>, so it has to go after
// them: "db.Users.AsNoTracking().FromSqlRaw(...)" does not compile (CS1061). The code-fix test
// harness compiles the fixed code, so each expected output below is proven to build.
public class MissingAsNoTrackingFromSqlFixerTests
{
    private const string Mocks = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public int SaveChanges() => 0;
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) where T : class => source;
    }

    public static class RelationalQueryableExtensions
    {
        public static IQueryable<T> FromSqlRaw<T>(this DbSet<T> source, string sql, params object[] parameters) where T : class => source;
        public static IQueryable<T> FromSqlInterpolated<T>(this DbSet<T> source, FormattableString sql) where T : class => source;
        public static IQueryable<T> FromSql<T>(this DbSet<T> source, FormattableString sql) where T : class => source;
    }

    public static class SqlServerDbSetExtensions
    {
        public static IQueryable<T> TemporalAll<T>(this DbSet<T> source) where T : class => source;
    }
}

namespace TestNamespace
{
    public class User { public int Id { get; set; } public string Name { get; set; } }

    public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    }

    public static class UserSetExtensions
    {
        // A project helper that only accepts the DbSet and hands back a list.
        public static List<User> Snapshot(this Microsoft.EntityFrameworkCore.DbSet<User> users) => new List<User>();
    }
}";

    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

    private static string Wrap(string body) => Usings + @"
class Program
{
    public void Run(MyDbContext db)
    {
        var name = ""admin"";
" + body + @"
    }
}
" + Mocks;

    [Fact]
    public async Task FromSqlRaw_AsNoTrackingGoesAfterTheRawSqlCall()
    {
        var test = Wrap(@"        var users = {|LC009:db.Users.FromSqlRaw(""SELECT * FROM Users WHERE Name = {0}"", name).ToList()|};");
        var fix = Wrap(@"        var users = db.Users.FromSqlRaw(""SELECT * FROM Users WHERE Name = {0}"", name).AsNoTracking().ToList();");

        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FromSql_AsNoTrackingGoesAfterTheRawSqlCall()
    {
        var test = Wrap(@"        var users = {|LC009:db.Users.FromSql($""SELECT * FROM Users WHERE Name = {name}"").ToList()|};");
        var fix = Wrap(@"        var users = db.Users.FromSql($""SELECT * FROM Users WHERE Name = {name}"").AsNoTracking().ToList();");

        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FromSqlInterpolated_WithLaterOperators_AsNoTrackingGoesRightAfterTheRawSqlCall()
    {
        var test = Wrap(@"        var user = {|LC009:db.Users.FromSqlInterpolated($""SELECT * FROM Users WHERE Name = {name}"").Where(u => u.Id > 0).OrderBy(u => u.Id).FirstOrDefault()|};");
        var fix = Wrap(@"        var user = db.Users.FromSqlInterpolated($""SELECT * FROM Users WHERE Name = {name}"").AsNoTracking().Where(u => u.Id > 0).OrderBy(u => u.Id).FirstOrDefault();");

        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FromSqlRaw_StaticCallForm_AsNoTrackingGoesAfterTheRawSqlCall()
    {
        var test = Wrap(@"        var users = {|LC009:RelationalQueryableExtensions.FromSqlRaw(db.Users, ""SELECT * FROM Users"").ToList()|};");
        var fix = Wrap(@"        var users = RelationalQueryableExtensions.FromSqlRaw(db.Users, ""SELECT * FROM Users"").AsNoTracking().ToList();");

        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task OtherDbSetOnlyOperator_AsNoTrackingGoesAfterIt()
    {
        // Not special-cased by name: any operator whose receiver must be a DbSet<T> moves the
        // insertion point after it.
        var test = Wrap(@"        var users = {|LC009:db.Users.TemporalAll().Where(u => u.Id > 0).ToList()|};");
        var fix = Wrap(@"        var users = db.Users.TemporalAll().AsNoTracking().Where(u => u.Id > 0).ToList();");

        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task DbSetOnlyHelperThatDoesNotReturnAQuery_NoFixOffered()
    {
        // Neither position compiles: before the helper, AsNoTracking() hides the DbSet it needs;
        // after it, there is a List<User> and no query. Report, but offer no fix.
        var test = Wrap(@"        var users = {|LC009:db.Users.Snapshot().ToList()|};");

        await VerifyCS.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task FixAll_RawSqlSampleShapes_AllCompile()
    {
        var test = Wrap(@"        var users1 = {|#0:db.Users.FromSqlRaw($""SELECT * FROM Users WHERE Name = '{name}'"").ToList()|};
        var users2 = {|#1:db.Users.FromSqlRaw(""SELECT * FROM Users WHERE Name = '"" + name + ""'"").ToList()|};
        var users3 = {|#2:db.Users.FromSqlRaw(parameters: Array.Empty<object>(), sql: $""SELECT * FROM Users WHERE Name = {name}"").ToList()|};
        var users4 = {|#3:db.Users.FromSqlRaw(""SELECT * FROM Users WHERE Name = {0}"", name).ToList()|};
        var users5 = {|#4:db.Users.FromSql($""SELECT * FROM Users WHERE Name = {name}"").ToList()|};
        var users6 = {|#5:db.Users.FromSqlInterpolated($""SELECT * FROM Users WHERE Name = {name}"").ToList()|};
        var users7 = {|#6:db.Users.Where(u => u.Id > 0).ToList()|};");

        var fixedCode = Wrap(@"        var users1 = db.Users.FromSqlRaw($""SELECT * FROM Users WHERE Name = '{name}'"").AsNoTracking().ToList();
        var users2 = db.Users.FromSqlRaw(""SELECT * FROM Users WHERE Name = '"" + name + ""'"").AsNoTracking().ToList();
        var users3 = db.Users.FromSqlRaw(parameters: Array.Empty<object>(), sql: $""SELECT * FROM Users WHERE Name = {name}"").AsNoTracking().ToList();
        var users4 = db.Users.FromSqlRaw(""SELECT * FROM Users WHERE Name = {0}"", name).AsNoTracking().ToList();
        var users5 = db.Users.FromSql($""SELECT * FROM Users WHERE Name = {name}"").AsNoTracking().ToList();
        var users6 = db.Users.FromSqlInterpolated($""SELECT * FROM Users WHERE Name = {name}"").AsNoTracking().ToList();
        var users7 = db.Users.AsNoTracking().Where(u => u.Id > 0).ToList();");

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 7,
            CodeFixEquivalenceKey = "AddAsNoTracking"
        };

        for (var i = 0; i < 7; i++)
            testObj.ExpectedDiagnostics.Add(
                new DiagnosticResult("LC009", DiagnosticSeverity.Info)
                    .WithArguments("Run")
                    .WithLocation(i));

        await testObj.RunAsync();
    }
}
