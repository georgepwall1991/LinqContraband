using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

public class MissingAsNoTrackingEdgeCasesTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

    private const string MockNamespace = @"
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
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
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
    public async Task ReadOnlyQuery_WithWriteInNestedLocalFunction_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        var db = new MyDbContext();

        void Persist()
        {
            db.SaveChanges();
        }

        return db.Users.ToList();
    }
}
" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReadOnlyQuery_WithWriteInNestedLambda_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        var db = new MyDbContext();
        Action persist = () => db.SaveChanges();
        return db.Users.ToList();
    }
}
" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
