using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC003_AnyOverCount.AnyOverCountAnalyzer,
    LinqContraband.Analyzers.LC003_AnyOverCount.AnyOverCountFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC003_AnyOverCount;

public class AnyOverCountFixerTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
";

    [Fact]
    public async Task CountGreaterThanZero_ShouldBeReplacedWithAny()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.Count() > 0|})
            {
            }
        }
    }
}";
        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Any())
            {
            }
        }
    }
}";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task CountWithPredicateGreaterThanZero_ShouldBeReplacedWithAnyPredicate()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.Count(x => x > 10) > 0|})
            {
            }
        }
    }
}";
        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Any(x => x > 10))
            {
            }
        }
    }
}";
        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task ZeroLessThanCount_ShouldBeReplacedWithAny()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:0 < query.Count()|})
            {
            }
        }
    }
}";
        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Any())
            {
            }
        }
    }
}";
        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task CountEqualsZero_ShouldBeReplacedWithNotAny()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.Count() == 0|})
            {
            }
        }
    }
}";
        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (!(query.Any()))
            {
            }
        }
    }
}";
        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task CountEqualsConstZero_ShouldBeReplacedWithNotAny()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private const int Empty = 0;

        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.Count() == Empty|})
            {
            }
        }
    }
}";
        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private const int Empty = 0;

        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (!(query.Any()))
            {
            }
        }
    }
}";
        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task CountEqualsZero_InReturnExpression_ShouldBeReplacedWithNotAny()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public bool TestMethod()
        {
            var query = new List<int>().AsQueryable();
            return {|LC003:query.Count() == 0|};
        }
    }
}";
        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public bool TestMethod()
        {
            var query = new List<int>().AsQueryable();
            return !(query.Any());
        }
    }
}";
        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task CountAsyncEqualsZero_ShouldBeReplacedWithNotAwaitAnyAsync()
    {
        var test = Usings + @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<int> CountAsync<TSource>(this IQueryable<TSource> source, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(0);
        public static Task<bool> AnyAsync<TSource>(this IQueryable<TSource> source, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}

namespace LinqContraband.Test
{
    public class TestClass
    {
        public async Task TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:await query.CountAsync() == 0|})
            {
            }
        }
    }
}";
        var fixedCode = Usings + @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<int> CountAsync<TSource>(this IQueryable<TSource> source, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(0);
        public static Task<bool> AnyAsync<TSource>(this IQueryable<TSource> source, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}

namespace LinqContraband.Test
{
    public class TestClass
    {
        public async Task TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (!(await query.AnyAsync()))
            {
            }
        }
    }
}";
        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task LongCountAsyncNotEqualsZero_InBooleanAssignment_ShouldBeReplacedWithAwaitAnyAsync()
    {
        var test = Usings + @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<long> LongCountAsync<TSource>(this IQueryable<TSource> source, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public static Task<bool> AnyAsync<TSource>(this IQueryable<TSource> source, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}

namespace LinqContraband.Test
{
    public class TestClass
    {
        public async Task TestMethod()
        {
            var query = new List<int>().AsQueryable();
            var hasAny = {|LC003:await query.LongCountAsync() != 0|};
        }
    }
}";
        var fixedCode = Usings + @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<long> LongCountAsync<TSource>(this IQueryable<TSource> source, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public static Task<bool> AnyAsync<TSource>(this IQueryable<TSource> source, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}

namespace LinqContraband.Test
{
    public class TestClass
    {
        public async Task TestMethod()
        {
            var query = new List<int>().AsQueryable();
            var hasAny = await query.AnyAsync();
        }
    }
}";
        await VerifyFix(test, fixedCode);
    }

    private static async Task VerifyFix(string test, string fixedCode)
    {
        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            CompilerDiagnostics = CompilerDiagnostics.None
        };

        // VerifyCS handles constructing the diagnostic expectation if using the helper,
        // but here we are using CodeFixTest directly.
        // The {|LC003:..|} syntax in TestCode handles the location assertion.

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixAll_ReplacesAllCountComparisonsWithAny()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|#0:query.Count() > 0|})
            {
            }
            if ({|#1:query.Count(x => x > 10) > 0|})
            {
            }
        }
    }
}";
        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Any())
            {
            }
            if (query.Any(x => x > 10))
            {
            }
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "ReplaceCountWithAny",
            CompilerDiagnostics = CompilerDiagnostics.None
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC003", DiagnosticSeverity.Warning)
                .WithLocation(0));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC003", DiagnosticSeverity.Warning)
                .WithLocation(1));

        await testObj.RunAsync();
    }
}
