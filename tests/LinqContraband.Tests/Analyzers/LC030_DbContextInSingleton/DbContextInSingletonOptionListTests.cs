using LinqContraband.Extensions;

namespace LinqContraband.Tests.Analyzers.LC030_DbContextInSingleton;

/// <summary>
/// <c>dotnet_code_quality.LC030.long_lived_types</c> holds a list. The editorconfig and globalconfig
/// parsers treat <c>;</c> and <c>#</c> as the start of an inline comment, so the list is separated
/// with commas. These tests go through the real config parsers.
/// </summary>
public partial class DbContextInSingletonTests
{
    private static Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
        LinqContraband.Analyzers.LC030_DbContextInSingleton.DbContextInSingletonAnalyzer,
        Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier> LongLivedTypesTest(
        bool globalConfig, string optionValue, bool interfaceWorkerReports, bool baseWorkerReports)
    {
        var interfaceField = interfaceWorkerReports ? "{|LC030:_db|}" : "_db";
        var baseField = baseWorkerReports ? "{|LC030:_db|}" : "_db";
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC030_DbContextInSingleton.DbContextInSingletonAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = EFCoreMock + @"
namespace TestApp
{
    public interface ILongLivedWorker { }
    public abstract class LongLivedBase { }

    public sealed class InterfaceWorker : ILongLivedWorker
    {
        private readonly Microsoft.EntityFrameworkCore.DbContext " + interfaceField + @";
    }

    public sealed class BaseWorker : LongLivedBase
    {
        private readonly Microsoft.EntityFrameworkCore.DbContext " + baseField + @";
    }
}
"
        };

        test.TestState.AnalyzerConfigFiles.Add(globalConfig
            ? ("/.globalconfig", "is_global = true\ndotnet_code_quality.LC030.long_lived_types = " + optionValue + "\n")
            : ("/0/.editorconfig", "root = true\n\n[*.cs]\ndotnet_code_quality.LC030.long_lived_types = " + optionValue + "\n"));

        return test;
    }

    [Theory]
    [InlineData(false, "TestApp.ILongLivedWorker, TestApp.LongLivedBase")]
    [InlineData(false, "Other.Thing,TestApp.ILongLivedWorker,TestApp.LongLivedBase")]
    [InlineData(false, "Other.Thing , TestApp.LongLivedBase ,, TestApp.ILongLivedWorker")]
    [InlineData(true, "TestApp.ILongLivedWorker, TestApp.LongLivedBase")]
    [InlineData(true, "Other.Thing, TestApp.LongLivedBase, TestApp.ILongLivedWorker")]
    public async Task ConfiguredLongLivedTypes_CommaSeparatedList_ShouldTriggerForEveryEntry(bool globalConfig, string optionValue)
    {
        await LongLivedTypesTest(globalConfig, optionValue, interfaceWorkerReports: true, baseWorkerReports: true).RunAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfiguredLongLivedTypes_SemicolonStartsConfigComment_OnlyFirstEntryApplies(bool globalConfig)
    {
        // The config parser drops everything from ';' on, so only TestApp.LongLivedBase reaches LC030.
        await LongLivedTypesTest(globalConfig, "TestApp.LongLivedBase;TestApp.ILongLivedWorker",
            interfaceWorkerReports: false, baseWorkerReports: true).RunAsync();
    }

    [Theory]
    [InlineData("A.B", new[] { "A.B" })]
    [InlineData(" A.B , C.D ", new[] { "A.B", "C.D" })]
    [InlineData("A.B;C.D", new[] { "A.B", "C.D" })]
    [InlineData("A.B; C.D, E.F", new[] { "A.B", "C.D", "E.F" })]
    [InlineData(" , ;; ", new string[0])]
    public void ListOptionValues_SplitOnCommasAndSemicolons(string value, string[] expected)
    {
        Assert.Equal(expected, AnalyzerConfigListOption.Split(value));
    }
}
