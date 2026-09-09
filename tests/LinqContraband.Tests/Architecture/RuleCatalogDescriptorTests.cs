using System;
using System.Linq;
using LinqContraband.Catalog;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace LinqContraband.Tests.Architecture;

public sealed class RuleCatalogDescriptorTests
{
    [Fact]
    public void LC046_CatalogContract_DescribesConcurrentDbContextOperations()
    {
        var rule = RuleCatalog.All.SingleOrDefault(entry => entry.Id == "LC046");

        Assert.True(rule != null, "LC046 should be present in the rule catalog.");
        Assert.Equal("LC046_ConcurrentDbContextOperations", rule!.Slug);
        Assert.Equal("Concurrent EF Core operations on the same DbContext", rule.Title);
        Assert.Equal("Safety", rule.Category);
        Assert.Equal("Execution & Async", rule.Domain);
        Assert.Equal(DiagnosticSeverity.Warning, rule.Severity);
        Assert.Equal("ConcurrentDbContextOperationsAnalyzer", rule.AnalyzerTypeName);
        Assert.False(rule.HasCodeFix);

        var analyzerAssembly =
            typeof(LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer).Assembly;
        var analyzerType = analyzerAssembly
            .GetTypes()
            .SingleOrDefault(type => type.Name == rule.AnalyzerTypeName);

        Assert.True(
            analyzerType != null,
            "LC046 should expose ConcurrentDbContextOperationsAnalyzer."
        );
    }

    [Fact]
    public void LC047_CatalogContract_DescribesExecuteDeleteTrackedDeletePipeline()
    {
        var rule = RuleCatalog.All.SingleOrDefault(entry => entry.Id == "LC047");

        Assert.True(rule != null, "LC047 should be present in the rule catalog.");
        Assert.Equal("LC047_ExecuteDeleteBypassesTrackedDelete", rule!.Slug);
        Assert.Equal("ExecuteDelete bypasses the tracked delete pipeline", rule.Title);
        Assert.Equal("Safety", rule.Category);
        Assert.Equal("Bulk Operations & Set-Based Writes", rule.Domain);
        Assert.Equal(DiagnosticSeverity.Warning, rule.Severity);
        Assert.Equal("ExecuteDeleteBypassesTrackedDeleteAnalyzer", rule.AnalyzerTypeName);
        Assert.Equal("ExecuteDeleteBypassesTrackedDeleteFixer", rule.FixerTypeName);
        Assert.True(rule.HasCodeFix);

        var analyzerAssembly =
            typeof(LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer).Assembly;
        var analyzerType = analyzerAssembly
            .GetTypes()
            .SingleOrDefault(type => type.Name == rule.AnalyzerTypeName);

        Assert.True(
            analyzerType != null,
            "LC047 should expose ExecuteDeleteBypassesTrackedDeleteAnalyzer."
        );
    }

    [Fact]
    public void LC048_CatalogContract_DescribesLostUpdateRisk()
    {
        var rule = RuleCatalog.All.SingleOrDefault(entry => entry.Id == "LC048");

        Assert.True(rule != null, "LC048 should be present in the rule catalog.");
        Assert.Equal("LC048_LostUpdateRisk", rule!.Slug);
        Assert.Equal("Tracked update can overwrite a concurrent change", rule.Title);
        Assert.Equal("Reliability", rule.Category);
        Assert.Equal("Change Tracking & Context Lifetime", rule.Domain);
        Assert.Equal(DiagnosticSeverity.Warning, rule.Severity);
        Assert.Equal("LostUpdateRiskAnalyzer", rule.AnalyzerTypeName);
        Assert.Null(rule.FixerTypeName);
        Assert.Equal("docs/LC048_LostUpdateRisk.md", rule.DocumentationPath);
        Assert.Equal(
            "samples/LinqContraband.Sample/Samples/LC048_LostUpdateRisk/LostUpdateRiskSample.cs",
            rule.SamplePath
        );
        Assert.Equal(
            "src/LinqContraband/Analyzers/ChangeTrackingAndContextLifetime/LC048_LostUpdateRisk",
            rule.AnalyzerSourcePath
        );
        Assert.False(rule.HasCodeFix);
        Assert.Equal(
            "No safe automated rewrite: concurrency tokens, atomic updates, and explicit transactions have different schema, retry, transaction, and behavioral semantics.",
            rule.NoCodeFixRationale
        );

        var analyzerAssembly =
            typeof(LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer).Assembly;
        var analyzerType = analyzerAssembly
            .GetTypes()
            .SingleOrDefault(type => type.Name == rule.AnalyzerTypeName);

        Assert.True(analyzerType != null, "LC048 should expose LostUpdateRiskAnalyzer.");

        var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType!)!;
        var descriptor = Assert.Single(analyzer.SupportedDiagnostics);
        Assert.Equal("LC048", descriptor.Id);
        Assert.Equal(
            "Tracked update can overwrite a concurrent change",
            descriptor.Title.ToString()
        );
        Assert.Equal(
            "'{0}' is computed from previously loaded state and can overwrite a concurrent change",
            descriptor.MessageFormat.ToString()
        );
        Assert.Equal("Reliability", descriptor.Category);
        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
        Assert.True(descriptor.IsEnabledByDefault);
        Assert.Equal(
            "A tracked entity property is read and then written before SaveChanges. Configure an optimistic concurrency token or use an atomic database update to prevent lost updates.",
            descriptor.Description.ToString()
        );
        Assert.Equal(
            "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC048_LostUpdateRisk.md",
            descriptor.HelpLinkUri
        );
    }

    [Fact]
    public void RuleCatalog_MatchesAnalyzerDescriptors()
    {
        var analyzerAssembly =
            typeof(LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer).Assembly;

        foreach (var rule in RuleCatalog.All)
        {
            var analyzerType = analyzerAssembly
                .GetTypes()
                .SingleOrDefault(type => type.Name == rule.AnalyzerTypeName);

            Assert.True(
                analyzerType != null,
                $"Could not find analyzer type '{rule.AnalyzerTypeName}' for {rule.Id}."
            );
            Assert.True(
                typeof(DiagnosticAnalyzer).IsAssignableFrom(analyzerType),
                $"Analyzer type '{rule.AnalyzerTypeName}' for {rule.Id} does not inherit from DiagnosticAnalyzer."
            );

            var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType!)!;
            var matchingDescriptors = analyzer
                .SupportedDiagnostics.Where(descriptor => descriptor.Id == rule.Id)
                .ToArray();

            Assert.NotEmpty(matchingDescriptors);
            var descriptor = matchingDescriptors[0];
            Assert.Equal(rule.Title, descriptor.Title.ToString());
            Assert.Equal(rule.Category, descriptor.Category);
            Assert.Equal(rule.Severity, descriptor.DefaultSeverity);

            var expectedHelpLink =
                $"https://github.com/georgepwall1991/LinqContraband/blob/master/{rule.DocumentationPath}";
            Assert.True(
                !string.IsNullOrWhiteSpace(descriptor.HelpLinkUri),
                $"{rule.Id}: descriptor must declare a helpLinkUri pointing at {expectedHelpLink}."
            );
            Assert.Equal(expectedHelpLink, descriptor.HelpLinkUri);
        }
    }
}
