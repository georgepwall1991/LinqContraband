using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC026_MissingCancellationToken.MissingCancellationTokenAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC026_MissingCancellationToken.MissingCancellationTokenAnalyzer,
    LinqContraband.Analyzers.LC026_MissingCancellationToken.MissingCancellationTokenFixer>;

namespace LinqContraband.Tests.Analyzers.LC026_MissingCancellationToken;

public class MissingCancellationTokenEdgeCasesTests
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
    }

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
    public async Task PreferredCancellationTokenName_IsUsedByFixer()
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
        public async Task TestMethod(DbSet<User> query, CancellationToken ct, CancellationToken cancellationToken)
        {
            var users = await query.ToListAsync();
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
        public async Task TestMethod(DbSet<User> query, CancellationToken ct, CancellationToken cancellationToken)
        {
            var users = await query.ToListAsync(cancellationToken);
        }
    }
}";

        var expected = VerifyFix.Diagnostic("LC026").WithSpan(36, 31, 36, 50).WithArguments("ToListAsync");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ShortCtName_IsUsedWhenCancellationTokenNameIsUnavailable()
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
        public async Task TestMethod(DbSet<User> query, CancellationToken requestAborted, CancellationToken ct)
        {
            var users = await {|LC026:query.ToListAsync()|};
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
        public async Task TestMethod(DbSet<User> query, CancellationToken requestAborted, CancellationToken ct)
        {
            var users = await query.ToListAsync(ct);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_AddsTokenToOuterAsyncInvocation_WhenReceiverIsQueryChain()
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
            var users = await {|LC026:query.Where(user => user.Id > 0).ToListAsync()|};
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
            var users = await query.Where(user => user.Id > 0).ToListAsync(cancellationToken);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task ReadablePropertyToken_IsUsedWhenNoParameterTokenExists()
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
        private CancellationToken RequestAborted { get; }

        public async Task TestMethod(DbSet<User> query)
        {
            var users = await {|LC026:query.ToListAsync()|};
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
        private CancellationToken RequestAborted { get; }

        public async Task TestMethod(DbSet<User> query)
        {
            var users = await query.ToListAsync(RequestAborted);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task FieldToken_ReplacesCancellationTokenNone_WhenNoCloserTokenExists()
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
        private CancellationToken _shutdownToken;

        public async Task TestMethod(DbSet<User> query)
        {
            var users = await {|LC026:query.ToListAsync(CancellationToken.None)|};
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
        private CancellationToken _shutdownToken;

        public async Task TestMethod(DbSet<User> query)
        {
            var users = await query.ToListAsync(_shutdownToken);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task NamedDefaultArgument_UsesPreferredTokenWhenMultipleTokensExist()
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
        public async Task TestMethod(DbSet<User> query, CancellationToken ct, CancellationToken cancellationToken)
        {
            var users = await {|LC026:query.ToListAsync(cancellationToken: default)|};
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
        public async Task TestMethod(DbSet<User> query, CancellationToken ct, CancellationToken cancellationToken)
        {
            var users = await query.ToListAsync(cancellationToken: cancellationToken);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task DefaultTokenInNestedLocalFunction_UsesNestedScopeToken()
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
        public Task TestMethod(DbSet<User> query)
        {
            async Task<List<User>> LoadAsync(CancellationToken ct)
            {
                return await {|LC026:query.ToListAsync(default)|};
            }

            return Task.CompletedTask;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ReplacesDefaultTokenArgument_InsteadOfAppending()
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
            var users = await {|LC026:query.ToListAsync(default)|};
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
            var users = await query.ToListAsync(cancellationToken);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ReplacesNamedDefaultTokenArgument_PreservingArgumentName()
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
            var users = await {|LC026:query.ToListAsync(cancellationToken: default)|};
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
        public async Task TestMethod(DbSet<User> query, CancellationToken ct)
        {
            var users = await query.ToListAsync(cancellationToken: ct);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ReplacesCancellationTokenNone_WhenUsableTokenIsAvailable()
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
            var users = await {|LC026:query.ToListAsync(CancellationToken.None)|};
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
            var users = await query.ToListAsync(cancellationToken);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }
}
