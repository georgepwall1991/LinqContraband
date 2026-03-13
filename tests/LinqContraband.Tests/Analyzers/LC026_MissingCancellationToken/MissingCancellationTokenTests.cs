using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC026_MissingCancellationToken.MissingCancellationTokenAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC026_MissingCancellationToken.MissingCancellationTokenAnalyzer,
    LinqContraband.Analyzers.LC026_MissingCancellationToken.MissingCancellationTokenFixer>;

namespace LinqContraband.Tests.Analyzers.LC026_MissingCancellationToken;

public class MissingCancellationTokenTests
{
    private const string EFCoreMock = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => Task.FromResult(new List<TSource>());
        public static Task<TSource> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => Task.FromResult(default(TSource));
        public static Task<int> SaveChangesAsync(this DbContext context, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    public class DbContext : IDisposable { public void Dispose() { } }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public System.Linq.Expressions.Expression Expression => null;
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        System.Collections.Generic.IEnumerator<TEntity> System.Collections.Generic.IEnumerable<TEntity>.GetEnumerator() => null;
    }
}
";

    [Fact]
    public async Task ToListAsync_WithoutToken_WithParameterInScope_ShouldTriggerLC026()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> query, CancellationToken ct)
        {
            var result = await {|LC026:query.ToListAsync()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToListAsync_WithoutToken_WithoutTokenInScope_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> query)
        {
            var result = await query.ToListAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToListAsync_DefaultArgument_WithTokenInScope_ShouldTriggerLC026()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> query, CancellationToken cancellationToken)
        {
            var result = await {|LC026:query.ToListAsync(default)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToListAsync_CancellationTokenNone_WithoutTokenInScope_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> query)
        {
            var result = await query.ToListAsync(CancellationToken.None);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToListAsync_WithoutToken_WithLocalTokenInScope_ShouldTriggerLC026()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> query)
        {
            using var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;
            var result = await {|LC026:query.ToListAsync()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_InsideAsyncLambda_UsesLambdaToken()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public Task Run(DbSet<User> query)
        {
            Func<CancellationToken, Task> work = async cancellationToken =>
            {
                _ = await {|LC026:query.FirstOrDefaultAsync()|};
            };

            return work(CancellationToken.None);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldPassAvailableToken()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> query, CancellationToken cancellationToken)
        {
            var result = await query.ToListAsync();
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> query, CancellationToken cancellationToken)
        {
            var result = await query.ToListAsync(cancellationToken);
        }
    }
}";

        var expected = VerifyFix.Diagnostic("LC026").WithSpan(40, 32, 40, 51).WithArguments("ToListAsync");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenNoTokenIsAvailable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> query)
        {
            var result = await query.ToListAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
