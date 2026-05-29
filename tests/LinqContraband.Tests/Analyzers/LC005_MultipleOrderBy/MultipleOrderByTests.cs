using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC005_MultipleOrderBy.MultipleOrderByAnalyzer,
    LinqContraband.Analyzers.LC005_MultipleOrderBy.MultipleOrderByFixer>;

namespace LinqContraband.Tests.Analyzers.LC005_MultipleOrderBy;

public class MultipleOrderByTests
{
    [Fact]
    public async Task NoDiagnostic_SingleOrderBy()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list.OrderBy(x => x);
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoDiagnostic_OrderByThenBy()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list.OrderBy(x => x).ThenBy(x => x);
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Diagnostic_OrderByOrderBy()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list.OrderBy(x => x).{|LC005:OrderBy|}(x => x);
    }
}";
        var fix = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list.OrderBy(x => x).ThenBy(x => x);
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task Diagnostic_OrderByOrderByDescending()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list.OrderBy(x => x).{|LC005:OrderByDescending|}(x => x);
    }
}";
        var fix = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list.OrderBy(x => x).ThenByDescending(x => x);
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task Diagnostic_ThenByOrderBy()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list.OrderBy(x => x).ThenBy(x => x).{|LC005:OrderBy|}(x => x);
    }
}";
        var fix = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list.OrderBy(x => x).ThenBy(x => x).ThenBy(x => x);
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task NoDiagnostic_QuerySyntax_SingleOrderBy()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = from x in list
                orderby x
                select x;
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoDiagnostic_QuerySyntax_MultiKeyOrderBy()
    {
        // A single orderby clause with multiple keys lowers to OrderBy(...).ThenBy(...),
        // which is correct multi-level sorting and must stay quiet (and must not crash).
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = from x in list
                orderby x, x descending
                select x;
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Diagnostic_QuerySyntax_TwoOrderByClauses()
    {
        // Two separate orderby clauses lower to OrderBy(...).OrderBy(...) — the second
        // resets the first. This is the same reset smell as the fluent form and must be
        // flagged, not crash the analyzer with an InvalidCastException (AD0001).
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = from x in list
                orderby x
                orderby {|LC005:x|}
                select x;
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Diagnostic_OrderByOrderBy_WithAsyncMaterializer()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(source.ToList());
    }
}

class Test
{
    async Task Method()
    {
        var q = await new List<int>().AsQueryable().OrderBy(x => x).{|LC005:OrderBy|}(x => x).ToListAsync();
    }
}";
        var fix = @"
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(source.ToList());
    }
}

class Test
{
    async Task Method()
    {
        var q = await new List<int>().AsQueryable().OrderBy(x => x).ThenBy(x => x).ToListAsync();
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }
}
