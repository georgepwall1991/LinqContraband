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
