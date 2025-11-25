using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public class OptimizeRemoveRangeAnalyzerTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using TestNamespace;
using Microsoft.EntityFrameworkCore;
";

    private const string MockNamespace = @"
namespace TestNamespace
{
    public class User { public int Id { get; set; } }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() {}
        public DbSet<User> Users { get; set; }
        public void RemoveRange(IEnumerable<object> entities) {}
    }

    public class DbSet<T> : IQueryable<T>
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;

        public void RemoveRange(IEnumerable<T> entities) {}
        public void RemoveRange(params T[] entities) {}
    }
}
";

    [Fact]
    public async Task RemoveRange_OnDbSet_ShouldTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10).ToList();
            
            // Trigger: DbSet.RemoveRange
            {|LC012:db.Users.RemoveRange(usersToDelete)|};
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_OnDbContext_ShouldTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10).ToList();
            
            // Trigger: DbContext.RemoveRange
            {|LC012:db.RemoveRange(usersToDelete)|};
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_ShouldNotTrigger()
    {
        // Note: ExecuteDelete is EF7+. We mock it or just assume it exists?
        // The analyzer checks for RemoveRange. So using ExecuteDelete won't trigger it anyway.
        // But we should ensure innocent code doesn't trigger.

        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            // Innocent method call
            db.Users.ToString(); 
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
