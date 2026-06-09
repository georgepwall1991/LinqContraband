using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowAnalyzer,
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowAnalyzer,
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowFixer>;

namespace LinqContraband.Tests.Analyzers.LC016_AvoidDateTimeNow;

public class AvoidDateTimeNowFixerTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
";

    [Fact]
    public async Task Fixer_ShouldExtractDateTimeNowToLocal()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var result = query.Where(x => x < {|LC016:DateTime.Now|}).ToList();
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
            var query = new List<DateTime>().AsQueryable();
            var now = DateTime.Now;
            var result = query.Where(x => x < now).ToList();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldExtractDateTimeUtcNowToLocal()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var result = query.Where(x => x < {|LC016:DateTime.UtcNow|}).ToList();
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
            var query = new List<DateTime>().AsQueryable();
            var now = DateTime.UtcNow;
            var result = query.Where(x => x < now).ToList();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_WhenNowParameterExists_ShouldUseUniqueLocalName()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(DateTime now)
        {
            var query = new List<DateTime>().AsQueryable();
            var result = query.Where(x => x < {|LC016:DateTime.Now|}).ToList();
        }
    }
}";

        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(DateTime now)
        {
            var query = new List<DateTime>().AsQueryable();
            var now1 = DateTime.Now;
            var result = query.Where(x => x < now1).ToList();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceRepeatedIdenticalClockUseInSameLambda()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var result = query.Where(x => x < {|LC016:DateTime.UtcNow|} && x > DateTime.UtcNow.AddDays(-1)).ToList();
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
            var query = new List<DateTime>().AsQueryable();
            var now = DateTime.UtcNow;
            var result = query.Where(x => x < now && x > now.AddDays(-1)).ToList();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task FixAll_RewritesAllDateTimeNowClockAccesses()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void FirstMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var result1 = query.Where(x => x < {|#0:DateTime.Now|}).ToList();
        }

        public void SecondMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var result2 = query.Where(x => x > {|#1:DateTime.Now|}).ToList();
        }
    }
}";

        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void FirstMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var now = DateTime.Now;
            var result1 = query.Where(x => x < now).ToList();
        }

        public void SecondMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var now = DateTime.Now;
            var result2 = query.Where(x => x > now).ToList();
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "ExtractToLocal"
        };

        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC016")
                .WithLocation(0)
                .WithArguments("DateTime.Now"));
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC016")
                .WithLocation(1)
                .WithArguments("DateTime.Now"));

        await testObj.RunAsync();
    }
}
