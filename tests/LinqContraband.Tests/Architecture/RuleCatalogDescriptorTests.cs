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

        var analyzerAssembly = typeof(LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer).Assembly;
        var analyzerType = analyzerAssembly.GetTypes()
            .SingleOrDefault(type => type.Name == rule.AnalyzerTypeName);

        Assert.True(analyzerType != null, "LC046 should expose ConcurrentDbContextOperationsAnalyzer.");
    }

    [Fact]
    public void RuleCatalog_MatchesAnalyzerDescriptors()
    {
        var analyzerAssembly = typeof(LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer).Assembly;

        foreach (var rule in RuleCatalog.All)
        {
            var analyzerType = analyzerAssembly.GetTypes()
                .SingleOrDefault(type => type.Name == rule.AnalyzerTypeName);

            Assert.True(analyzerType != null, $"Could not find analyzer type '{rule.AnalyzerTypeName}' for {rule.Id}.");
            Assert.True(typeof(DiagnosticAnalyzer).IsAssignableFrom(analyzerType),
                $"Analyzer type '{rule.AnalyzerTypeName}' for {rule.Id} does not inherit from DiagnosticAnalyzer.");

            var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType!)!;
            var matchingDescriptors = analyzer.SupportedDiagnostics
                .Where(descriptor => descriptor.Id == rule.Id)
                .ToArray();

            Assert.NotEmpty(matchingDescriptors);
            var descriptor = matchingDescriptors[0];
            Assert.Equal(rule.Title, descriptor.Title.ToString());
            Assert.Equal(rule.Category, descriptor.Category);
            Assert.Equal(rule.Severity, descriptor.DefaultSeverity);

            var expectedHelpLink = $"https://github.com/georgepwall1991/LinqContraband/blob/master/{rule.DocumentationPath}";
            Assert.True(!string.IsNullOrWhiteSpace(descriptor.HelpLinkUri),
                $"{rule.Id}: descriptor must declare a helpLinkUri pointing at {expectedHelpLink}.");
            Assert.Equal(expectedHelpLink, descriptor.HelpLinkUri);
        }
    }
}
