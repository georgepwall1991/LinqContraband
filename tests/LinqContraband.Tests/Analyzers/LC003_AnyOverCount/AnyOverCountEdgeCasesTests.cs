using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC003_AnyOverCount.AnyOverCountAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC003_AnyOverCount;

public class AnyOverCountEdgeCasesTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
";

    [Fact]
    public async Task CountGreaterThanOrEqualToOne_OnIQueryable_ShouldTriggerLC003()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.Count() >= 1|})
            {
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OneLessThanOrEqualToCount_OnIQueryable_ShouldTriggerLC003()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:1 <= query.Count()|})
            {
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CountNotEqualToZero_OnIQueryable_ShouldTriggerLC003()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.Count() != 0|})
            {
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CountGreaterThanZero_InBooleanAssignment_ShouldTriggerLC003()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            var hasAny = {|LC003:query.Count() > 0|};
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CountEqualsZero_InReturnExpression_ShouldTriggerLC003()
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
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LongCountAsyncNotEqualsZero_InBooleanAssignment_ShouldTriggerLC003()
    {
        var test = Usings + @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<long> LongCountAsync<TSource>(this IQueryable<TSource> source, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(0L);
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
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ZeroNotEqualToCount_OnIQueryable_ShouldTriggerLC003()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:0 != query.Count()|})
            {
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CountGreaterThanOne_OnIQueryable_ShouldNotTriggerLC003()
    {
        // Count() > 1 is NOT the same as checking for existence (Any())
        // This should NOT trigger the analyzer
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Count() > 1)
            {
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CountGreaterThanOrEqualToTwo_OnIQueryable_ShouldNotTriggerLC003()
    {
        // Count() >= 2 is NOT checking for existence
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Count() >= 2)
            {
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CountGreaterThanNegativeOne_OnIQueryable_ShouldNotTriggerLC003()
    {
        // Count() > -1 is always true but not an existence check pattern
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Count() > -1)
            {
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CountEqualsZero_OnIQueryable_ShouldTriggerLC003()
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
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ZeroEqualsCount_OnIQueryable_ShouldTriggerLC003()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:0 == query.Count()|})
            {
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
