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

    private static string Program(string attribute, string mapping = "", string members = "") => Usings + @"
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
    " + members + @"

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

    // The MethodInfo goes through a local or a readonly field first (fullstackhero's NetTopologySuite mappings).
    [Theory]
    [InlineData("var isAdult = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); modelBuilder.HasDbFunction(isAdult!);")]
    [InlineData("var isAdult = typeof(Rules).GetMethod(\"IsAdult\", new[] { typeof(int) }); modelBuilder.HasDbFunction(isAdult);")]
    [InlineData("System.Reflection.MethodInfo isAdult = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); modelBuilder.HasDbFunction(isAdult);")]
    [InlineData("var isAdult = System.Reflection.RuntimeReflectionExtensions.GetRuntimeMethod(typeof(Rules), nameof(Rules.IsAdult), new[] { typeof(int) }); modelBuilder.HasDbFunction(isAdult);")]
    [InlineData("var isAdult = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); Console.WriteLine(isAdult); modelBuilder.HasDbFunction(isAdult);")]
    public Task DbFunctionMappingThroughLocal_IsQuiet(string mapping) =>
        VerifyCS.VerifyAnalyzerAsync(Program("", mapping));

    [Theory]
    [InlineData("private static readonly System.Reflection.MethodInfo IsAdultMethod = typeof(Rules).GetMethod(nameof(Rules.IsAdult));")]
    [InlineData("private readonly System.Reflection.MethodInfo IsAdultMethod = typeof(Rules).GetMethod(\"IsAdult\", new[] { typeof(int) });")]
    [InlineData("static readonly System.Reflection.MethodInfo IsAdultMethod = System.Reflection.RuntimeReflectionExtensions.GetRuntimeMethod(typeof(Rules), nameof(Rules.IsAdult), new[] { typeof(int) });")]
    public Task DbFunctionMappingThroughReadonlyField_IsQuiet(string field) =>
        VerifyCS.VerifyAnalyzerAsync(Program("", "modelBuilder.HasDbFunction(IsAdultMethod);", field));

    [Theory]
    // The local maps a different method.
    [InlineData("var isAdult = typeof(Rules).GetMethod(nameof(Rules.IsSenior)); modelBuilder.HasDbFunction(isAdult);")]
    // The local is reassigned, so the mapped method is not known.
    [InlineData("var isAdult = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); isAdult = typeof(Rules).GetMethod(nameof(Rules.IsSenior)); modelBuilder.HasDbFunction(isAdult);")]
    [InlineData("var isAdult = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); Action reset = () => isAdult = null; modelBuilder.HasDbFunction(isAdult);")]
    [InlineData("var isAdult = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); Replace(ref isAdult); modelBuilder.HasDbFunction(isAdult);")]
    [InlineData("var isAdult = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); (isAdult, _) = (typeof(Rules).GetMethod(nameof(Rules.IsSenior)), 0); modelBuilder.HasDbFunction(isAdult);")]
    // The initializer is not typeof(T).GetMethod(constant).
    [InlineData("var isAdult = Find(); modelBuilder.HasDbFunction(isAdult);")]
    [InlineData("var name = Console.ReadLine(); var isAdult = typeof(Rules).GetMethod(name); modelBuilder.HasDbFunction(isAdult);")]
    [InlineData("var first = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); var isAdult = first; modelBuilder.HasDbFunction(isAdult);")]
    [InlineData("System.Reflection.MethodInfo isAdult; isAdult = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); modelBuilder.HasDbFunction(isAdult);")]
    public Task DbFunctionMappingThroughUnknownLocal_StillReports(string mapping) =>
        VerifyCS.VerifyAnalyzerAsync(
            Program("", mapping, @"
    static System.Reflection.MethodInfo Find() => typeof(Rules).GetMethod(nameof(Rules.IsAdult));
    static void Replace(ref System.Reflection.MethodInfo method) => method = null;").Replace(
                "u => Rules.IsAdult(u.Age)", "u => {|LC001:Rules.IsAdult(u.Age)|}"));

    [Theory]
    // A mutable field can be set from anywhere.
    [InlineData("private static System.Reflection.MethodInfo IsAdultMethod = typeof(Rules).GetMethod(nameof(Rules.IsAdult));")]
    // A readonly field assigned again in a constructor.
    [InlineData("private static readonly System.Reflection.MethodInfo IsAdultMethod = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); static Mapping() { IsAdultMethod = typeof(Rules).GetMethod(nameof(Rules.IsSenior)); }")]
    [InlineData("private readonly System.Reflection.MethodInfo IsAdultMethod = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); Mapping(bool other) { if (other) IsAdultMethod = null; }")]
    // No initializer, or one that is not typeof(T).GetMethod(constant).
    [InlineData("private static readonly System.Reflection.MethodInfo IsAdultMethod; static Mapping() { IsAdultMethod = typeof(Rules).GetMethod(nameof(Rules.IsAdult)); }")]
    [InlineData("private static readonly System.Reflection.MethodInfo IsAdultMethod = typeof(Rules).GetMethod(nameof(Rules.IsSenior));")]
    [InlineData("private static readonly System.Reflection.MethodInfo IsAdultMethod = typeof(Rules).GetMethods()[0];")]
    public Task DbFunctionMappingThroughUnknownField_StillReports(string field) =>
        VerifyCS.VerifyAnalyzerAsync(
            Program("", "modelBuilder.HasDbFunction(IsAdultMethod);", field).Replace(
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
