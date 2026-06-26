using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC029_RedundantIdentitySelect.RedundantIdentitySelectAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC029_RedundantIdentitySelect;

public class RedundantIdentitySelectTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
";

    [Fact]
    public async Task Select_Identity_ShouldTriggerLC029()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int> query)
        {
            var result = {|LC029:query.Select(x => x)|}.ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_ParenthesizedReceiverIdentity_ShouldTriggerLC029()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int> query)
        {
            var result = {|LC029:(query).Select(x => x)|}.ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_CastReceiverIdentity_ShouldTriggerLC029()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(object query)
        {
            var result = {|LC029:((IQueryable<int>)query).Select(x => x)|}.ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_NullForgivingReceiverIdentity_ShouldTriggerLC029()
    {
        var test = Usings + @"
#nullable enable
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int>? query)
        {
            var result = {|LC029:query!.Select(x => x)|}.ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_BlockBodyIdentity_ShouldTriggerLC029()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IEnumerable<int> query)
        {
            var result = {|LC029:query.Select(x => { return x; })|}.ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConcreteEnumerableSelect_BlockBodyIdentity_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(List<int> query)
        {
            var result = query.Select(x => { return x; }).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_NonIdentity_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int> query)
        {
            var result = query.Select(x => x.ToString()).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_TypeChangingConversion_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IEnumerable<int> query)
        {
            var result = query.Select(x => (object)x).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_AwaitedTaskProjection_ShouldNotTrigger()
    {
        var test = Usings + @"
using System.Threading.Tasks;
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IEnumerable<Task<int>> query)
        {
            var result = query.Select(async x => await x).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_ExplicitCastProjection_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class BaseType { }
    public class DerivedType : BaseType { }

    public class TestClass
    {
        public void TestMethod(IEnumerable<BaseType> query)
        {
            var result = query.Select<BaseType, BaseType>(x => (DerivedType)x).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticEnumerableSelect_Identity_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IEnumerable<int> query)
        {
            var result = Enumerable.Select(query, x => x).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticEnumerableSelect_BlockBodyIdentity_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IEnumerable<int> query)
        {
            var result = Enumerable.Select(query, x => { return x; }).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
