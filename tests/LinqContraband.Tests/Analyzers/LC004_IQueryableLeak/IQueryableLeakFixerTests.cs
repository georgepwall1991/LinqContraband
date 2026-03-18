using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC004_IQueryableLeak.IQueryableLeakAnalyzer,
    LinqContraband.Analyzers.LC004_IQueryableLeak.IQueryableLeakFixer>;

namespace LinqContraband.Tests.Analyzers.LC004_IQueryableLeak;

public class IQueryableLeakFixerTests
{
    private const string Usings = @"
using System;
using System.Collections;
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
    public async Task Fixer_ShouldMaterializePlainArgument()
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

            ProcessUsers({|#0:query|});
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

        var fixedCode = Usings + @"
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

            ProcessUsers(query.ToList());
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

        var expected = VerifyFix.Diagnostic("LC004").WithLocation(0).WithArguments("users", "IEnumerable<User>");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldMaterializeNamedArgument()
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

            ProcessUsers(users: {|#0:query|});
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

        var fixedCode = Usings + @"
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

            ProcessUsers(users: query.ToList());
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

        var expected = VerifyFix.Diagnostic("LC004").WithLocation(0).WithArguments("users", "IEnumerable<User>");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldPreserveParenthesizedQueryExpression()
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

            ProcessUsers(({|#0:db.Users.Where(u => u.Id > 10)|}));
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

        var fixedCode = Usings + @"
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

            ProcessUsers((db.Users.Where(u => u.Id > 10)).ToList());
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

        var expected = VerifyFix.Diagnostic("LC004").WithLocation(0).WithArguments("users", "IEnumerable<User>");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_ForNonGenericQuerySource()
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
            IQueryable query = db.Users.Where(u => u.Id > 10);

            ProcessUsers({|#0:query|});
        }

        private static void ProcessUsers(IEnumerable users)
        {
            foreach (var user in users)
            {
                Console.WriteLine(user);
            }
        }
    }
}
" + MockNamespace;

        var expected = VerifyFix.Diagnostic("LC004").WithLocation(0).WithArguments("users", "IEnumerable");
        await VerifyFix.VerifyCodeFixAsync(test, expected, test);
    }
}
