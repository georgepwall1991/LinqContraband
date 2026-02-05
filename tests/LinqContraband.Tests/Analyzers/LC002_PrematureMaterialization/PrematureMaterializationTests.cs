using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer>;
using LinqContraband.Analyzers.LC002_PrematureMaterialization;

namespace LinqContraband.Tests.Analyzers.LC002_PrematureMaterialization;

public class PrematureMaterializationTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using TestNamespace;
using Microsoft.EntityFrameworkCore;
";

    private const string MockNamespace = @"
namespace TestNamespace
{
    public class User
    {
        public int Age { get; set; }
    }

    public class DbContext
    {
        public IQueryable<User> Users => new List<User>().AsQueryable();
    }
}

namespace Microsoft.EntityFrameworkCore
{
    public static class AsyncExtensions
    {
        public static System.Threading.Tasks.Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => System.Threading.Tasks.Task.FromResult(source.ToList());
    }
}";

    [Fact]
    public async Task TestCrime_ToListBeforeWhere_ShouldTriggerLC002()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.ToList().Where(x => x.Age > 18);
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithSpan(13, 21, 13, 61) // Where call spans from 'db.Users...'
            .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_WhereBeforeToList_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Where(x => x.Age > 18).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestMemory_ListWhere_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var list = new List<User>();
        var query = list.ToList().Where(x => x.Age > 18);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ToDictionaryBeforeWhere_ShouldTriggerLC002()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.ToDictionary(x => x.Age).Where(u => u.Value.Age > 18);
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithSpan(13, 21, 13, 83)
            .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_AsEnumerableBeforeWhere_ShouldTriggerLC002()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.AsEnumerable().Where(u => u.Age > 18);
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithSpan(13, 21, 13, 67)
            .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_AwaitToListAsyncBeforeWhere_ShouldTriggerLC002()
    {
        var test = Usings + @"
using System.Threading.Tasks;

class Program
{
    async Task Main()
    {
        var db = new DbContext();
        var query = (await db.Users.ToListAsync()).Where(x => x.Age > 18);
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithSpan(15, 21, 15, 74)
            .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ToImmutableListBeforeWhere_ShouldTriggerLC002()
    {
        var test = Usings + @"
using System.Collections.Immutable;

class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.ToImmutableList().Where(x => x.Age > 18);
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
             .WithSpan(15, 21, 15, 70)
             .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AsEnumerable_ThenToList_ShouldTriggerLC002_Redundant()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var result = db.Users.AsEnumerable().ToList();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithSpan(13, 22, 13, 54)
            .WithArguments("ToList", "AsEnumerable");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ToList_ThenToArray_ShouldTriggerLC002_Redundant()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var result = db.Users.ToList().ToArray();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithSpan(13, 22, 13, 49)
            .WithArguments("ToArray", "ToList");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
