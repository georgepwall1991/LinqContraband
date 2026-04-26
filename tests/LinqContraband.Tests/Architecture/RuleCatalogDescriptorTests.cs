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
            if (!string.IsNullOrWhiteSpace(descriptor.HelpLinkUri))
                Assert.Equal(expectedHelpLink, descriptor.HelpLinkUri);
        }
    }
}
