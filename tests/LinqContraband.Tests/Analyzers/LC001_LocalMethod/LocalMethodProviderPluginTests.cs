using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC001_LocalMethod;

/// <summary>
/// Methods an EF Core provider plugin translates, such as pgvector's <c>CosineDistance</c> (eShop's semantic
/// search), and namespaces a project lists in <c>dotnet_code_quality.LC001.trusted_namespaces</c> for its own
/// or a third-party <c>IMethodCallTranslator</c>.
/// </summary>
public class LocalMethodProviderPluginTests
{
    private const string Mocks = @"
namespace TestNamespace
{
    public class Item
    {
        public int Id { get; set; }
        public Pgvector.Vector Embedding { get; set; }
        public string Name { get; set; }
    }

    public class DbContext
    {
        public IQueryable<Item> Items => new List<Item>().AsQueryable();
    }
}

namespace Pgvector
{
    public class Vector { }
}

namespace Pgvector.EntityFrameworkCore
{
    public static class VectorDbFunctionsExtensions
    {
        public static double CosineDistance(this Pgvector.Vector a, Pgvector.Vector b) => 0;
        public static double L2Distance(this Pgvector.Vector a, Pgvector.Vector b) => 0;
    }
}

namespace MyCompany.Translators
{
    public static class JsonFunctions
    {
        public static string JsonValue(string json, string path) => json;
    }
}

namespace MyCompany.Translators.Text
{
    public static class TextFunctions
    {
        public static string Soundex(string value) => value;
    }
}

namespace MyCompany.TranslatorsExtra
{
    public static class Lookalike
    {
        public static string Soundex(string value) => value;
    }
}
";

    private static string Program(string body) => @"
using System;
using System.Linq;
using System.Collections.Generic;
using Pgvector.EntityFrameworkCore;
using TestNamespace;

class Program
{
    void Run(DbContext db, Pgvector.Vector vector)
    {
        " + body + @"
    }
}
" + Mocks;

    [Theory]
    [InlineData("var q = db.Items.OrderBy(c => c.Embedding.CosineDistance(vector)).Take(10).ToList();")]
    [InlineData("var q = db.Items.Select(c => new { Item = c, Distance = c.Embedding.CosineDistance(vector) }).ToList();")]
    [InlineData("var q = db.Items.Where(c => c.Embedding.L2Distance(vector) < 0.5).ToList();")]
    [InlineData("var q = db.Items.OrderBy(c => VectorDbFunctionsExtensions.CosineDistance(c.Embedding, vector)).ToList();")]
    public Task PgvectorDistanceFunction_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    [Fact]
    public Task UnconfiguredTranslatorNamespace_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(
            "var q = db.Items.Where(c => {|LC001:MyCompany.Translators.JsonFunctions.JsonValue(c.Name, \"$.a\")|} == \"x\").ToList();"));

    [Theory]
    [InlineData("MyCompany.Translators")]
    [InlineData("Other.Namespace, MyCompany.Translators")]
    public Task ConfiguredTrustedNamespace_IsQuiet(string optionValue) =>
        RunWithOptionAsync(
            optionValue,
            "var q = db.Items.Where(c => MyCompany.Translators.JsonFunctions.JsonValue(c.Name, \"$.a\") == \"x\").ToList();" +
            " var r = db.Items.Where(c => MyCompany.Translators.Text.TextFunctions.Soundex(c.Name) == \"x\").ToList();");

    [Fact]
    public Task ConfiguredTrustedNamespace_DoesNotTrustLookalikePrefix() =>
        RunWithOptionAsync(
            "MyCompany.Translators",
            "var q = db.Items.Where(c => {|LC001:MyCompany.TranslatorsExtra.Lookalike.Soundex(c.Name)|} == \"x\").ToList();");

    private static async Task RunWithOptionAsync(string optionValue, string body)
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = Program(body)
        };
        test.TestState.AnalyzerConfigFiles.Add(("/0/.editorconfig", $@"root = true

[*.cs]
dotnet_code_quality.LC001.trusted_namespaces = {optionValue}
"));

        await test.RunAsync();
    }
}
