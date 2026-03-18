using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC004_IQueryableLeak.IQueryableLeakAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC004_IQueryableLeak;

public class IQueryableLeakTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace TestNamespace
{
    public class User
    {
        public int Id { get; set; }
    }

    public class DbContext : IDisposable
    {
        public void Dispose() { }
    }

    public class DbSet<T> : IQueryable<T>
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }
}
";

    [Fact]
    public async Task Leak_WhenForeachConsumesParameter_ShouldTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var query = db.Users.Where(u => u.Id > 10);

            ProcessUsers({|LC004:query|});
        }

        private static void ProcessUsers(IEnumerable<User> users)
        {
            foreach (var user in users)
            {
                Console.WriteLine(user.Id);
            }
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Leak_WhenEnumerableTerminalConsumesParameter_ShouldTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public bool Main()
        {
            using var db = new AppDbContext();
            var query = db.Users.Where(u => u.Id > 10);

            return HasUsers({|LC004:query|});
        }

        private static bool HasUsers(IEnumerable<User> users)
        {
            return users.Where(u => u.Id > 50).Any();
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Leak_WhenExpressionBodiedMethodConsumesParameter_ShouldTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public bool Main()
        {
            using var db = new AppDbContext();
            var query = db.Users.Where(u => u.Id > 10);

            return HasUsers({|LC004:query|});
        }

        private static bool HasUsers(IEnumerable<User> users) => users.Any();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Leak_WhenWrapperForwardsToHazardousMethod_ShouldTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var query = db.Users.Where(u => u.Id > 10);

            Wrapper({|LC004:query|});
        }

        private static void Wrapper(IEnumerable<User> users)
        {
            Consume(users);
        }

        private static void Consume(IEnumerable<User> users)
        {
            foreach (var user in users)
            {
                Console.WriteLine(user.Id);
            }
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoLeak_WhenMethodDoesNotConsumeParameter_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public IEnumerable<User> Main()
        {
            using var db = new AppDbContext();
            var query = db.Users.Where(u => u.Id > 10);

            return Tap(query);
        }

        private static IEnumerable<User> Tap(IEnumerable<User> users)
        {
            Console.WriteLine(nameof(users));
            return users;
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoLeak_WhenPassingToIQueryableMethod_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public IQueryable<User> Main()
        {
            using var db = new AppDbContext();
            var query = db.Users.Where(u => u.Id > 10);

            return ProcessUsersQuery(query);
        }

        private static IQueryable<User> ProcessUsersQuery(IQueryable<User> users)
        {
            return users.Where(u => u.Id > 20);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoLeak_WhenPassingToFrameworkMethod_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public string Main()
        {
            using var db = new AppDbContext();

            return string.Join("","", db.Users.Where(u => u.Id > 10).Select(u => u.Id));
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoLeak_WhenMethodHasNoSourceBody_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public interface IUserProcessor
    {
        void Process(IEnumerable<User> users);
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public void Main(IUserProcessor processor)
        {
            using var db = new AppDbContext();
            var query = db.Users.Where(u => u.Id > 10);

            processor.Process(query);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoLeak_WhenCallingDelegateInvoke_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var query = db.Users.Where(u => u.Id > 10);
            Action<IEnumerable<User>> sink = users =>
            {
                foreach (var user in users)
                {
                    Console.WriteLine(user.Id);
                }
            };

            sink(query);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoLeak_WhenArgumentIsAlreadyMaterialized_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();

            ProcessUsers(db.Users.Where(u => u.Id > 10).ToList());
        }

        private static void ProcessUsers(IEnumerable<User> users)
        {
            foreach (var user in users)
            {
                Console.WriteLine(user.Id);
            }
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
