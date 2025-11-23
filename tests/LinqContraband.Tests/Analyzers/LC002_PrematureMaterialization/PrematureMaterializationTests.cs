using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer>;

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

        var expected = VerifyCS.Diagnostic("LC002")
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

        var expected = VerifyCS.Diagnostic("LC002")
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

        var expected = VerifyCS.Diagnostic("LC002")
            .WithSpan(13, 21, 13, 67)
            .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    // Async materialization coverage is TODO; current analyzer focuses on sync and awaits unwrapped cases.
}
