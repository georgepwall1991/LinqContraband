using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC002_PrematureMaterialization;

public class PrematureMaterializationTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using TestNamespace;
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
            .WithSpan(12, 21, 12, 61) // Where call spans from 'db.Users...'
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
}