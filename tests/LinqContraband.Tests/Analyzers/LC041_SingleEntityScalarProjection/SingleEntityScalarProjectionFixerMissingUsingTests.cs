using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionAnalyzer,
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionFixer>;

namespace LinqContraband.Tests.Analyzers.LC041_SingleEntityScalarProjection;

public partial class SingleEntityScalarProjectionTests
{
    // A queryable DbSet whose async terminals live in Microsoft.EntityFrameworkCore, like EF Core's. The
    // mock keeps its own usings inside its namespace so the call site sees only what the test declares.
    private const string QueryableEfCoreMock = @"
namespace Microsoft.EntityFrameworkCore
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading;
    using System.Threading.Tasks;

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<TSource> FirstAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => Task.FromResult(default(TSource));
        public static Task<TSource> FirstAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(default(TSource));
    }
}
";

    [Fact]
    public async Task Fixer_AsyncCallWithoutSystemLinqUsing_AddsTheUsing()
    {
        // FirstAsync binds through the EF Core using alone; the Where/Select the fix inserts need System.Linq.
        var test = @"using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
" + QueryableEfCoreMock + @"
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
            var user = await {|LC041:users.FirstAsync(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);
        }
    }
}";

        var fixedCode = @"using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Linq;
" + QueryableEfCoreMock + @"
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
            var user = await users.Where(x => x.IsActive).Select(x => x.Name).FirstAsync();
            System.Console.WriteLine(user);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_AsyncCallWithSystemLinqUsing_AddsNoUsing()
    {
        var test = @"using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
" + QueryableEfCoreMock + @"
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
            var user = await {|LC041:users.FirstAsync(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);
        }
    }
}";

        var fixedCode = @"using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
" + QueryableEfCoreMock + @"
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
            var user = await users.Where(x => x.IsActive).Select(x => x.Name).FirstAsync();
            System.Console.WriteLine(user);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }
}
