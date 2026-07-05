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
    public async Task Fixer_WhenQueryMethodIsExpressionBodied_ShouldConvertToBlock()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public IQueryable<DateTime> Recent(IQueryable<DateTime> query) =>
            query.Where(x => x < {|LC016:DateTime.Now|});
    }
}";

        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public IQueryable<DateTime> Recent(IQueryable<DateTime> query)
        {
            var now = DateTime.Now;
            return query.Where(x => x < now);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task FixAll_WhenExpressionBodiedMethodHasMultipleQueryLambdas_ShouldRewriteWholeMember()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public IQueryable<DateTime> Recent(IQueryable<DateTime> query) =>
            query.Where(x => x < {|#0:DateTime.Now|}).Concat(query.Where(x => x > {|#1:DateTime.UtcNow|}));
    }
}";

        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public IQueryable<DateTime> Recent(IQueryable<DateTime> query)
        {
            var now = DateTime.Now;
            var now1 = DateTime.UtcNow;
            return query.Where(x => x < now).Concat(query.Where(x => x > now1));
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            CodeFixEquivalenceKey = "ExtractToLocal"
        };

        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC016")
                .WithLocation(0)
                .WithArguments("DateTime.Now"));
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC016")
                .WithLocation(1)
                .WithArguments("DateTime.UtcNow"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task Fixer_WhenExpressionBodiedMethodContainsTrivia_ShouldPreserveTrivia()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public IQueryable<DateTime> Recent(IQueryable<DateTime> query) =>
            query.Where(x => x < {|LC016:DateTime.Now|} /* keep deterministic-window note */);
    }
}";

        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public IQueryable<DateTime> Recent(IQueryable<DateTime> query)
        {
            var now = DateTime.Now;
            return query.Where(x => x < now /* keep deterministic-window note */);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_WhenVoidQueryMethodIsExpressionBodied_ShouldConvertToExpressionStatement()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void Recent(IQueryable<DateTime> query) =>
            query.Where(x => x < {|LC016:DateTime.Now|}).ToList();
    }
}";

        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void Recent(IQueryable<DateTime> query)
        {
            var now = DateTime.Now;
            query.Where(x => x < now).ToList();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_WhenAsyncTaskQueryMethodIsExpressionBodied_ShouldConvertToExpressionStatement()
    {
        var usings = Usings + @"
using System.Threading.Tasks;
";

        var test = usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public async Task Recent(IQueryable<DateTime> query) =>
            await Task.FromResult(query.Where(x => x < {|LC016:DateTime.UtcNow|}).ToList());
    }
}";

        var fixedCode = usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public async Task Recent(IQueryable<DateTime> query)
        {
            var now = DateTime.UtcNow;
            await Task.FromResult(query.Where(x => x < now).ToList());
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_WhenAliasedAsyncTaskQueryMethodIsExpressionBodied_ShouldConvertToExpressionStatement()
    {
        var usings = Usings + @"
using System.Threading.Tasks;
using Work = System.Threading.Tasks.Task;
";

        var test = usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public async Work Recent(IQueryable<DateTime> query) =>
            await Task.FromResult(query.Where(x => x < {|LC016:DateTime.UtcNow|}).ToList());
    }
}";

        var fixedCode = usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public async Work Recent(IQueryable<DateTime> query)
        {
            var now = DateTime.UtcNow;
            await Task.FromResult(query.Where(x => x < now).ToList());
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_WhenQueryLocalFunctionIsExpressionBodied_ShouldConvertToBlock()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<DateTime> query)
        {
            IQueryable<DateTime> Recent() =>
                query.Where(x => x < {|LC016:DateTime.UtcNow|});
        }
    }
}";

        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<DateTime> query)
        {
            IQueryable<DateTime> Recent()
            {
                var now = DateTime.UtcNow;
                return query.Where(x => x < now);
            }
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_WhenQueryPropertyIsExpressionBodied_ShouldNotOfferFix()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private IQueryable<DateTime> query = new List<DateTime>().AsQueryable();

        public IQueryable<DateTime> Recent =>
            query.Where(x => x < {|LC016:DateTime.Now|});
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_WhenExpressionBodiedQueryLambdaIsStatic_ShouldNotOfferFix()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public IQueryable<DateTime> Recent(IQueryable<DateTime> query) =>
            query.Where(static x => x < {|LC016:DateTime.Now|});
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
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
    public async Task Fixer_WhenStatementQueryLambdaIsStatic_ShouldNotOfferFix()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var result = query.Where(static x => x < {|LC016:DateTime.Now|}).ToList();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
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
