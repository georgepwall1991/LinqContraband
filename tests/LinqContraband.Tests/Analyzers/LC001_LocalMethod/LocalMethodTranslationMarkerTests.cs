using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC001_LocalMethod;

/// <summary>
/// Methods that EF Core or a query-expansion library knows how to translate: attributes from LINQKit,
/// NeinLinq and DelegateDecompiler, attributes a project lists in <c>dotnet_code_quality.LC001.trusted_attributes</c>,
/// and methods mapped with <c>modelBuilder.HasDbFunction(...)</c>.
/// </summary>
public class LocalMethodTranslationMarkerTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace TestNamespace
{
    public class User
    {
        public int Age { get; set; }
        public string Name { get; set; }
    }

    public class DbContext
    {
        public IQueryable<User> Users => new List<User>().AsQueryable();
    }
}

namespace Microsoft.EntityFrameworkCore
{
    public class ModelBuilder
    {
        public object HasDbFunction(System.Reflection.MethodInfo methodInfo) => null;
        public object HasDbFunction<TResult>(System.Linq.Expressions.Expression<Func<TResult>> expression) => null;
    }
}

namespace LinqKit
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class ExpandableAttribute : Attribute
    {
        public ExpandableAttribute(string methodName) { }
    }
}

namespace NeinLinq
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class InjectLambdaAttribute : Attribute { }
}

namespace DelegateDecompiler
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class ComputedAttribute : Attribute { }
}

namespace MyCompany.Data
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SqlTranslatableAttribute : Attribute { }
}

namespace Fake
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ExpandableAttribute : Attribute
    {
        public ExpandableAttribute(string methodName) { }
    }
}";

    private static string Program(string attribute, string mapping = "") => Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Where(u => Rules.IsAdult(u.Age));
    }
}

static class Rules
{
    " + attribute + @"
    public static bool IsAdult(int age) => age >= 18;

    public static bool IsSenior(int age) => age >= 65;
}

class Mapping
{
    void Configure(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        " + mapping + @"
    }
}
" + MockNamespace;

    [Theory]
    [InlineData("[LinqKit.Expandable(nameof(IsAdult))]")]
    [InlineData("[NeinLinq.InjectLambda]")]
    [InlineData("[DelegateDecompiler.Computed]")]
    public Task QueryExpansionLibraryMarker_IsQuiet(string attribute) =>
        VerifyCS.VerifyAnalyzerAsync(Program(attribute));

    [Fact]
    public Task LookalikeMarkerFromAnotherNamespace_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program("[Fake.Expandable(nameof(IsAdult))]").Replace(
                "u => Rules.IsAdult(u.Age)", "u => {|LC001:Rules.IsAdult(u.Age)|}"));

    [Theory]
    [InlineData("modelBuilder.HasDbFunction(typeof(Rules).GetMethod(nameof(Rules.IsAdult)));")]
    [InlineData("modelBuilder.HasDbFunction(typeof(Rules).GetMethod(\"IsAdult\", new[] { typeof(int) }));")]
    [InlineData("modelBuilder.HasDbFunction(() => Rules.IsAdult(default));")]
    public Task FluentDbFunctionMapping_IsQuiet(string mapping) =>
        VerifyCS.VerifyAnalyzerAsync(Program("", mapping));

    [Theory]
    // A different method is mapped.
    [InlineData("modelBuilder.HasDbFunction(typeof(Rules).GetMethod(nameof(Rules.IsSenior)));")]
    [InlineData("modelBuilder.HasDbFunction(() => Rules.IsSenior(default));")]
    // The name is only known at run time.
    [InlineData("var name = Console.ReadLine(); modelBuilder.HasDbFunction(typeof(Rules).GetMethod(name));")]
    public Task OtherOrUnknownDbFunctionMapping_StillReports(string mapping) =>
        VerifyCS.VerifyAnalyzerAsync(
            Program("", mapping).Replace(
                "u => Rules.IsAdult(u.Age)", "u => {|LC001:Rules.IsAdult(u.Age)|}"));

    [Theory]
    [InlineData("MyCompany.Data.SqlTranslatableAttribute")]
    [InlineData("MyCompany.Data.SqlTranslatable")]
    [InlineData("Other.Attribute, MyCompany.Data.SqlTranslatable")]
    public async Task ConfiguredTrustedAttribute_IsQuiet(string optionValue)
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = Program("[MyCompany.Data.SqlTranslatable]")
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $@"is_global = true
dotnet_code_quality.LC001.trusted_attributes = {optionValue}
"));

        await test.RunAsync();
    }

    // Goes through the .editorconfig parser, where ';' and '#' start an inline comment, so
    // the list is comma-separated and the trusted attribute can sit anywhere in it.
    [Theory]
    [InlineData("Other.One, Other.Two, MyCompany.Data.SqlTranslatable")]
    [InlineData("Other.One,MyCompany.Data.SqlTranslatable,Other.Two")]
    public async Task ConfiguredTrustedAttributeList_InEditorConfig_IsQuiet(string optionValue)
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = Program("[MyCompany.Data.SqlTranslatable]")
        };
        test.TestState.AnalyzerConfigFiles.Add(("/0/.editorconfig", $@"root = true

[*.cs]
dotnet_code_quality.LC001.trusted_attributes = {optionValue}
"));

        await test.RunAsync();
    }

    [Fact]
    public Task UnconfiguredProjectAttribute_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program("[MyCompany.Data.SqlTranslatable]").Replace(
                "u => Rules.IsAdult(u.Age)", "u => {|LC001:Rules.IsAdult(u.Age)|}"));

    [Fact]
    public async Task ConfiguredTrustedAttribute_DoesNotTrustOtherAttributes()
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = Program("[MyCompany.Data.SqlTranslatable]").Replace(
                "u => Rules.IsAdult(u.Age)", "u => {|LC001:Rules.IsAdult(u.Age)|}")
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", @"is_global = true
dotnet_code_quality.LC001.trusted_attributes = MyCompany.Data.OtherAttribute
"));

        await test.RunAsync();
    }
}
