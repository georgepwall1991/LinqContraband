using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowAnalyzer,
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowAnalyzer,
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowFixer>;

namespace LinqContraband.Tests.Analyzers.LC016_AvoidDateTimeNow;

// Shapes where applying the fix to BTCPay Server, Bitwarden and a probe project broke the build, threw,
// or changed behaviour.
public class AvoidDateTimeNowFixerSafetyTests
{
    private static string Code(string members) => @"
using System;
using System.Linq;

namespace LinqContraband.Test
{
    public class TestClass
    {
" + members + @"
    }
}";

    [Fact]
    public async Task Fixer_AvoidsNameUsedInEnclosingBlock()
    {
        var test = Code(@"
        public object Run(IQueryable<DateTime> query, bool flag)
        {
            var now = DateTime.UtcNow.AddDays(-1);
            if (flag)
            {
                return query.Where(x => x < {|LC016:DateTime.Now|}).ToList();
            }

            return now;
        }");
        var fixedCode = Code(@"
        public object Run(IQueryable<DateTime> query, bool flag)
        {
            var now = DateTime.UtcNow.AddDays(-1);
            if (flag)
            {
                var now1 = DateTime.Now;
                return query.Where(x => x < now1).ToList();
            }

            return now;
        }");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_DoesNotShadowField()
    {
        var test = Code(@"
        private DateTime now = DateTime.MinValue;

        public object Run(IQueryable<DateTime> query)
        {
            var result = query.Where(x => x < {|LC016:DateTime.Now|}).ToList();
            return now;
        }");
        var fixedCode = Code(@"
        private DateTime now = DateTime.MinValue;

        public object Run(IQueryable<DateTime> query)
        {
            var now1 = DateTime.Now;
            var result = query.Where(x => x < now1).ToList();
            return now;
        }");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_WrapsEmbeddedStatementInBlock()
    {
        var test = Code(@"
        public object Run(IQueryable<DateTime> query, bool flag)
        {
            if (flag)
                return query.Where(x => x < {|LC016:DateTime.Now|}).ToList();

            return null;
        }");
        var fixedCode = Code(@"
        public object Run(IQueryable<DateTime> query, bool flag)
        {
            if (flag)
            {
                var now = DateTime.Now;
                return query.Where(x => x < now).ToList();
            }

            return null;
        }");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_LoopCondition_ReportsWithoutFix()
    {
        // Hoisting the read out of the condition would freeze the time the loop keeps checking.
        var test = Code(@"
        public void Run(IQueryable<DateTime> query)
        {
            while (query.Any(x => x > {|LC016:DateTime.UtcNow|}))
            {
                System.Threading.Thread.Sleep(10);
            }
        }");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ReplacesEveryReadInQuerySyntax()
    {
        var test = Code(@"
        public object Run(IQueryable<DateTime> starts)
        {
            return (from s in starts
                    where s <= {|LC016:DateTime.UtcNow|} && s.AddDays(1) >= DateTime.UtcNow
                    select s).ToList();
        }");
        var fixedCode = Code(@"
        public object Run(IQueryable<DateTime> starts)
        {
            var now = DateTime.UtcNow;
            return (from s in starts
                    where s <= now && s.AddDays(1) >= now
                    select s).ToList();
        }");

        // LC016 reports the query once; the fix used to replace only that read, so it reported again.
        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task FixAll_DeclaresDistinctLocalsInSwitchSections()
    {
        var test = Code(@"
        public object Run(IQueryable<DateTime> query, int state)
        {
            switch (state)
            {
                case 0:
                    return query.Where(x => x < {|LC016:DateTime.UtcNow|}).ToList();
                case 1:
                    return query.Where(x => x > {|LC016:DateTime.UtcNow|}).ToList();
                default:
                    return null;
            }
        }");
        var fixedCode = Code(@"
        public object Run(IQueryable<DateTime> query, int state)
        {
            switch (state)
            {
                case 0:
                    var now = DateTime.UtcNow;
                    return query.Where(x => x < now).ToList();
                case 1:
                    var now1 = DateTime.UtcNow;
                    return query.Where(x => x > now1).ToList();
                default:
                    return null;
            }
        }");

        await new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            NumberOfFixAllIterations = 1,
        }.RunAsync();
    }
}
