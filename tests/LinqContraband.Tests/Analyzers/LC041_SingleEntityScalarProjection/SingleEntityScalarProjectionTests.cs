using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC041_SingleEntityScalarProjection;

public partial class SingleEntityScalarProjectionTests
{
    private const string EfCoreMock = @"
using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<TEntity>
    {
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static DbSet<TSource> Where<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate) => source;
        public static DbSet<TResult> Select<TSource, TResult>(this DbSet<TSource> source, Expression<Func<TSource, TResult>> selector) => new DbSet<TResult>();
        public static TSource First<TSource>(this DbSet<TSource> source) => default(TSource);
        public static TSource First<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate) => default(TSource);
        public static TSource FirstOrDefault<TSource>(this DbSet<TSource> source) => default(TSource);
        public static TSource FirstOrDefault<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate) => default(TSource);
        public static TSource Single<TSource>(this DbSet<TSource> source) => default(TSource);
        public static TSource Single<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate) => default(TSource);
        public static TSource SingleOrDefault<TSource>(this DbSet<TSource> source) => default(TSource);
        public static TSource SingleOrDefault<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate) => default(TSource);
        public static Task<TSource> FirstAsync<TSource>(this DbSet<TSource> source, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(default(TSource));
        public static Task<TSource> FirstAsync<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(default(TSource));
        public static Task<TSource> FirstOrDefaultAsync<TSource>(this DbSet<TSource> source, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(default(TSource));
        public static Task<TSource> FirstOrDefaultAsync<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(default(TSource));
        public static Task<TSource> SingleAsync<TSource>(this DbSet<TSource> source, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(default(TSource));
        public static Task<TSource> SingleAsync<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(default(TSource));
        public static Task<TSource> SingleOrDefaultAsync<TSource>(this DbSet<TSource> source, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(default(TSource));
        public static Task<TSource> SingleOrDefaultAsync<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(default(TSource));
    }
}
";

    [Fact]
    public async Task FirstOrDefault_WithSinglePropertyUsage_Triggers()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = {|LC041:users.FirstOrDefault(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingleAsync_WithPropertyChainUsage_Triggers()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public async Task Run(DbSet<User> users)
        {
            var user = await {|LC041:users.SingleAsync(x => x.IsActive)|};
            System.Console.WriteLine(user.Name.Length);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PrimaryKeyLookup_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = users.FirstOrDefault(x => x.Id == 1);
            System.Console.WriteLine(user.Name);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PrimaryKeyLookupViaWhereStep_ShouldNotTrigger()
    {
        // Where(x => x.Id == id).First() is the same single-row-by-key fetch as First(x => x.Id == id),
        // which is already exempt — the PK guard must inspect the Where step in the chain too.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = users.Where(x => x.Id == 1).First();
            System.Console.WriteLine(user.Name);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonKeyWhereStepThenFirst_ShouldStillTrigger()
    {
        // A non-key Where (x.IsActive) is not a primary-key lookup, so the single-property over-fetch
        // is still reported — guards against over-suppressing the new Where-chain PK exemption.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = {|LC041:users.Where(x => x.IsActive).First()|};
            System.Console.WriteLine(user.Name);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultiplePropertyUsage_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = users.FirstOrDefault(x => x.IsActive);
            System.Console.WriteLine(user.Name);
            System.Console.WriteLine(user.Email);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PropertyAssignment_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = users.First(x => x.IsActive);
            user.Name = ""Updated"";
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EntityPassedToMethod_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = users.First(x => x.IsActive);
            Touch(user);
            System.Console.WriteLine(user.Name);
        }

        private static void Touch(User user) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

}
