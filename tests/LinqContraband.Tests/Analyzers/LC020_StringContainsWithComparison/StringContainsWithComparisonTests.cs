using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC020_StringContainsWithComparison.StringContainsWithComparisonAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC020_StringContainsWithComparison.StringContainsWithComparisonAnalyzer,
    LinqContraband.Analyzers.LC020_StringContainsWithComparison.StringContainsWithComparisonFixer>;

namespace LinqContraband.Tests.Analyzers.LC020_StringContainsWithComparison;

public class StringContainsWithComparisonTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
";

    [Fact]
    public async Task StringContains_WithComparison_InWhere_ShouldTriggerLC020()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<string>().AsQueryable();
            var result = query.Where(x => {|LC020:x.Contains(""abc"", StringComparison.OrdinalIgnoreCase)|}).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldRemoveStringComparison()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<string>().AsQueryable();
            var result = query.Where(x => {|LC020:x.Contains(""abc"", StringComparison.OrdinalIgnoreCase)|}).ToList();
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
            var query = new List<string>().AsQueryable();
            var result = query.Where(x => x.Contains(""abc"")).ToList();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task StringStartsWith_WithComparison_InFirstOrDefault_ShouldTriggerLC020()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<string>().AsQueryable();
            var result = query.FirstOrDefault(x => {|LC020:x.StartsWith(""abc"", StringComparison.CurrentCulture)|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringEndsWith_WithComparison_InAny_ShouldTriggerLC020()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<string>().AsQueryable();
            var result = query.Any(x => {|LC020:x.EndsWith("".org"", StringComparison.OrdinalIgnoreCase)|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringContains_WithComparison_InNestedCollectionPredicate_ShouldTriggerLC020()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class User
    {
        public List<Order> Orders { get; set; } = new();
    }

    public class Order
    {
        public string Number { get; set; } = """";
    }

    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<User>().AsQueryable();
            var result = query.Where(u => u.Orders.Any(o => {|LC020:o.Number.Contains(""rush"", StringComparison.OrdinalIgnoreCase)|})).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringContains_WithComparison_InNestedLocalEnumerablePredicateInsideQueryable_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class User
    {
        public string Name { get; set; } = """";
    }

    public class TestClass
    {
        public void TestMethod()
        {
            var tags = new List<string> { ""admin"" };
            var query = new List<User>().AsQueryable();
            var result = query.Where(u => tags.Any(tag => tag.Contains(""a"", StringComparison.OrdinalIgnoreCase))).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringContains_WithoutComparison_InWhere_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<string>().AsQueryable();
            var result = query.Where(x => x.Contains(""abc"")).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringContains_WithComparison_OnCapturedLocalInsideQueryable_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var needle = ""abc"";
            var query = new List<string>().AsQueryable();
            var result = query.Where(x => needle.Contains(""a"", StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringContains_WithComparison_OnConstantInsideQueryable_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<string>().AsQueryable();
            var result = query.Where(x => ""abc"".Contains(""a"", StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringContains_WithComparison_InLocalCode_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var s = ""test"";
            var result = s.Contains(""t"", StringComparison.OrdinalIgnoreCase);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringContains_WithComparison_InCustomIQueryableFuncExtension_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public static class QueryExtensions
    {
        public static IQueryable<T> WhereCustom<T>(this IQueryable<T> source, Func<T, bool> predicate) => source;
    }

    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<string>().AsQueryable();
            var result = query.WhereCustom(x => x.Contains(""abc"", StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringContains_WithComparison_InEnumerable_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var list = new List<string>();
            var result = list.Where(x => x.Contains(""abc"", StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ForStartsWith_ShouldRemoveStringComparisonArgument()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<string>().AsQueryable();
            var result = query.Where(x => {|LC020:x.StartsWith(""abc"", StringComparison.OrdinalIgnoreCase)|}).ToList();
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
            var query = new List<string>().AsQueryable();
            var result = query.Where(x => x.StartsWith(""abc"")).ToList();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }
}
