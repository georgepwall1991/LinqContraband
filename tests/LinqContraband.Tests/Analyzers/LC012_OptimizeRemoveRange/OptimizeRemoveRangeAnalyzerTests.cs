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

    private const string MockNamespaceWithoutExecuteDelete = @"
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
        public void RemoveRange(params object[] entities) {}
        public int SaveChanges() => 0;
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

    private const string ExecuteDeleteSupport = @"
namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
    }
}
";

    private const string MockNamespace = MockNamespaceWithoutExecuteDelete + ExecuteDeleteSupport;

    [Fact]
    public async Task RemoveRange_OnDbSetWithQueryableSource_ShouldTrigger()
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
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            
            // Trigger: DbSet.RemoveRange
            {|LC012:db.Users.RemoveRange(usersToDelete)|};
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_OnDbContextWithQueryableSource_ShouldTrigger()
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
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            
            // Trigger: DbContext.RemoveRange
            {|LC012:db.RemoveRange(usersToDelete)|};
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_FollowedBySaveChanges_ShouldNotTrigger()
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
            var usersToDelete = db.Users.Where(u => u.Id > 10);

            db.Users.RemoveRange(usersToDelete);
            db.SaveChanges();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_InNestedBlockFollowedByOuterSaveChanges_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main(bool shouldDelete)
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);

            if (shouldDelete)
            {
                db.Users.RemoveRange(usersToDelete);
            }

            db.SaveChanges();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithMaterializedList_ShouldNotTrigger()
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
            db.Users.RemoveRange(usersToDelete);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WhenExecuteDeleteIsUnavailable_ShouldNotTrigger()
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
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            db.Users.RemoveRange(usersToDelete);
        }
    }
}" + MockNamespaceWithoutExecuteDelete;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithCustomExecuteDeleteExtension_ShouldNotTrigger()
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
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            db.Users.RemoveRange(usersToDelete);
        }
    }
}" + MockNamespaceWithoutExecuteDelete + @"
namespace CustomExtensions
{
    public static class QueryExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithExecuteDeleteInLookalikeNamespace_ShouldNotTrigger()
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
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            db.Users.RemoveRange(usersToDelete);
        }
    }
}" + MockNamespaceWithoutExecuteDelete + @"
namespace Microsoft.EntityFrameworkCoreFake
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithParamsArray_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main(User user)
        {
            using var db = new AppDbContext();
            db.Users.RemoveRange(user);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextRemoveRange_WithQueryAndTrackedEntity_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main(User user)
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            db.RemoveRange(usersToDelete, user);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextRemoveRange_WithTwoQueryArguments_ShouldNotTrigger()
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
            var oldUsers = db.Users.Where(u => u.Id > 10);
            var newerUsers = db.Users.Where(u => u.Id <= 10);
            db.RemoveRange(oldUsers, newerUsers);
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
