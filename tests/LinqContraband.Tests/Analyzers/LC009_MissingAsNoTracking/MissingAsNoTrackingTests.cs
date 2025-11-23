using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

public class MissingAsNoTrackingTests
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
    public class UserDto { public int Id { get; set; } }
    
    public class MyDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }
}";

    [Fact]
    public async Task TestCrime_ReadOnlyMethod_ReturnsEntities_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        var db = new MyDbContext();
        // Crime: Returning entities, no SaveChanges, no AsNoTracking
        return db.Users.Where(u => u.Id > 0).ToList();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC009")
            .WithSpan(15, 16, 15, 54) // Matches db.Users.Where(...).ToList()
            .WithArguments("GetUsers");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_WriteMethod_CallingSaveChanges_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void UpdateUser()
    {
        var db = new MyDbContext();
        var users = db.Users.ToList(); // Tracking needed here
        db.SaveChanges();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ReadOnlyMethod_WithAsNoTracking_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        var db = new MyDbContext();
        return db.Users.AsNoTracking().Where(u => u.Id > 0).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ReadOnlyMethod_WithAsNoTrackingWithIdentityResolution_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        var db = new MyDbContext();
        return db.Users.AsNoTrackingWithIdentityResolution().ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ReadOnlyMethod_WithSelect_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public List<UserDto> GetDtos()
    {
        var db = new MyDbContext();
        // Projection implies no tracking needed anyway
        return db.Users.Select(u => new UserDto { Id = u.Id }).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ExplicitAsTracking_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        var db = new MyDbContext();
        // Explicit opt-in
        return db.Users.AsTracking().ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
