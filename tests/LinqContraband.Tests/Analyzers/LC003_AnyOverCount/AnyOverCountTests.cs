using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC003_AnyOverCount.AnyOverCountAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC003_AnyOverCount;

public class AnyOverCountTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
";

    private const string MockNamespace = @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            // Placeholder
        }
    }
}";

    [Fact]
    public async Task CountGreaterThanZero_OnIQueryable_ShouldTriggerLC003()
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

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CountWithPredicateGreaterThanZero_OnIQueryable_ShouldTriggerLC003()
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

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LongCountGreaterThanZero_OnIQueryable_ShouldTriggerLC003()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.LongCount() > 0|})
            {
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ZeroLessThanCount_OnIQueryable_ShouldTriggerLC003()
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
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CountGreaterThanZero_OnList_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var list = new List<int>();
            if (list.Count > 0)
            {
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CountEqualsZero_ShouldNotTrigger_ForNow()
    {
        // Analyzer is specifically for Any() replacement which implies > 0. 
        // == 0 would be !Any(), which is a valid extension but let's stick to the requirement first.
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Count() == 0)
            {
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }



    [Fact]
    public async Task CountAsync_GreaterThanZero_ShouldTriggerLC003()
    {
        // Let's use direct expression:
        var test2 = Usings + @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<int> CountAsync<TSource>(this IQueryable<TSource> source, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
namespace LinqContraband.Test
{
    public class TestClass
    {
        public async Task TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (await query.CountAsync() > 0)
            {
            }
        }
    }
}
";

        // Line calculation:
        // Usings (6 lines)
        // L7: using System.Threading.Tasks;
        // L8: namespace MF...
        // L9: {
        // L10: public static...
        // L11:     public static...
        // L12: }
        // L13: }
        // L14: namespace LC.Test
        // L15: {
        // L16: class
        // L17: {
        // L18: method
        // L19: {
        // L20: var query...
        // L21: if (await query.CountAsync() > 0)

        var expected = VerifyCS.Diagnostic("LC003")
            .WithSpan(23, 17, 23, 45);

        await VerifyCS.VerifyAnalyzerAsync(test2, expected);
    }

    [Fact]
    public async Task ZeroLessThanCountAsync_ShouldTriggerLC003()
    {
        var test = Usings + @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<int> CountAsync<TSource>(this IQueryable<TSource> source, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
namespace LinqContraband.Test
{
    public class TestClass
    {
        public async Task TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (0 < await query.CountAsync())
            {
            }
        }
    }
}
";

        var expected = VerifyCS.Diagnostic("LC003")
            .WithSpan(23, 17, 23, 45);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
