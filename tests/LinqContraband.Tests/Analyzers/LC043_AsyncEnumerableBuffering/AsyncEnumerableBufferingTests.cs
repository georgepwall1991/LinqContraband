using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC043_AsyncEnumerableBuffering.AsyncEnumerableBufferingAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC043_AsyncEnumerableBuffering.AsyncEnumerableBufferingAnalyzer,
    LinqContraband.Analyzers.LC043_AsyncEnumerableBuffering.AsyncEnumerableBufferingFixer>;

namespace LinqContraband.Tests.Analyzers.LC043_AsyncEnumerableBuffering;

public class AsyncEnumerableBufferingTests
{
    private const string AsyncEnumerableMock = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public static class AsyncEnumerableExtensions
    {
        public static Task<List<TSource>> ToListAsync<TSource>(this IAsyncEnumerable<TSource> source) => Task.FromResult(new List<TSource>());
        public static Task<List<TSource>> ToListAsync<TSource>(this IAsyncEnumerable<TSource> source, CancellationToken cancellationToken) => Task.FromResult(new List<TSource>());
        public static Task<TSource[]> ToArrayAsync<TSource>(this IAsyncEnumerable<TSource> source) => Task.FromResult(new TSource[0]);
    }
}
";

    [Fact]
    public async Task BufferedAsyncEnumerable_ImmediatelyLooped_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + AsyncEnumerableMock + @"
namespace TestApp
{
    public class User { public string Name { get; set; } }

    public class TestClass
    {
        public async Task Run(IAsyncEnumerable<User> users)
        {
            var items = await users.ToListAsync();
            foreach (var item in items)
            {
                System.Console.WriteLine(item.Name);
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC043").WithSpan(26, 31, 26, 50).WithArguments("ToListAsync");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task BufferedArray_ImmediatelyLooped_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + AsyncEnumerableMock + @"
namespace TestApp
{
    public class User { public string Name { get; set; } }

    public class TestClass
    {
        public async Task Run(IAsyncEnumerable<User> users)
        {
            var items = await users.ToArrayAsync();
            foreach (var item in items)
            {
                System.Console.WriteLine(item.Name);
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC043").WithSpan(26, 31, 26, 51).WithArguments("ToArrayAsync");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Fixer_ShouldConvertImmediateBufferingToAwaitForeach()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + AsyncEnumerableMock + @"
namespace TestApp
{
    public class User { public string Name { get; set; } }

    public class TestClass
    {
        public async Task Run(IAsyncEnumerable<User> users)
        {
            var items = await users.ToListAsync();
            foreach (var item in items)
            {
                System.Console.WriteLine(item.Name);
            }
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + AsyncEnumerableMock + @"
namespace TestApp
{
    public class User { public string Name { get; set; } }

    public class TestClass
    {
        public async Task Run(IAsyncEnumerable<User> users)
        {
            await foreach (var item in users)
            {
                System.Console.WriteLine(item.Name);
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC043").WithSpan(26, 31, 26, 50).WithArguments("ToListAsync");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task BufferedAsyncEnumerable_WithInterveningStatement_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + AsyncEnumerableMock + @"
namespace TestApp
{
    public class User { public string Name { get; set; } }

    public class TestClass
    {
        public async Task Run(IAsyncEnumerable<User> users)
        {
            var items = await users.ToListAsync();
            System.Console.WriteLine(""middle"");
            foreach (var item in items)
            {
                System.Console.WriteLine(item.Name);
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task BufferedAsyncEnumerable_WithCancellationToken_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;" + AsyncEnumerableMock + @"
namespace TestApp
{
    public class User { public string Name { get; set; } }

    public class TestClass
    {
        public async Task Run(IAsyncEnumerable<User> users, CancellationToken cancellationToken)
        {
            var items = await users.ToListAsync(cancellationToken);
            foreach (var item in items)
            {
                System.Console.WriteLine(item.Name);
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task BufferedAsyncEnumerable_WithSecondUse_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + AsyncEnumerableMock + @"
namespace TestApp
{
    public class User { public string Name { get; set; } }

    public class TestClass
    {
        public async Task Run(IAsyncEnumerable<User> users)
        {
            var items = await users.ToListAsync();
            foreach (var item in items)
            {
                System.Console.WriteLine(item.Name);
            }

            System.Console.WriteLine(items.Count);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CustomToListAsync_OnNonAsyncEnumerable_ShouldNotTrigger()
    {
        var test = @"using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestApp
{
    public class User { public string Name { get; set; } }

    public sealed class Repository
    {
        public Task<List<User>> ToListAsync() => Task.FromResult(new List<User>());
    }

    public class TestClass
    {
        public async Task Run(Repository repository)
        {
            var items = await repository.ToListAsync();
            foreach (var item in items)
            {
                System.Console.WriteLine(item.Name);
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
