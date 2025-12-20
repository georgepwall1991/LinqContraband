using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LinqContraband.Analyzers.LC002_PrematureMaterialization;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC002_PrematureMaterialization;

public class PrematureMaterializationEdgeCasesTests
{
    [Fact]
    public async Task NewList_ThenWhere_ShouldTriggerLC002()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;

class Program
{
    void Main()
    {
        var query = new List<int>().AsQueryable();
        var result = new List<int>(query).Where(x => x > 0);
    }
}";

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithSpan(10, 22, 10, 60)
            .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NewHashSet_ThenCount_ShouldTriggerLC002()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;

class Program
{
    void Main()
    {
        var query = new List<int>().AsQueryable();
        var result = new HashSet<int>(query).Count();
    }
}";

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithSpan(10, 22, 10, 53)
            .WithArguments("Count");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}