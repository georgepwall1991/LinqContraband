using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest =
    Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
        LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer,
        LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingFixer,
        Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

public class MissingAsNoTrackingFixerTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public DbSet<T> Set<T>() where T : class => new DbSet<T>();
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
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
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsNoTrackingWithIdentityResolution<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsTracking<T>(this IQueryable<T> source) => source;
    }
}

namespace TestNamespace
{
    public class User { public int Id { get; set; } }
    
    public class MyDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }
}";

    [Fact]
    public async Task FixCrime_InjectsAsNoTracking()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        var db = new MyDbContext();
        return db.Users.Where(u => u.Id > 0).ToList();
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        var db = new MyDbContext();
        return db.Users.AsNoTracking().Where(u => u.Id > 0).ToList();
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
        };

        // Line 14
        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC009", DiagnosticSeverity.Warning)
            .WithSpan(14, 16, 14, 54)
            .WithArguments("GetUsers"));

        await testObj.RunAsync();
    }
}
