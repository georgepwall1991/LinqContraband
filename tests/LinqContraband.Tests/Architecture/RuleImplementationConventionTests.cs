using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LinqContraband.Catalog;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace LinqContraband.Tests.Architecture;

public sealed class RuleImplementationConventionTests
{
    private readonly string _repoRoot = RepositoryLayout.GetRepositoryRoot();

    [Fact]
    public void AnalyzersAndFixers_AreSealed()
    {
        var analyzerAssembly = typeof(LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer).Assembly;

        var offenders = analyzerAssembly.GetTypes()
            .Where(type => !type.IsAbstract
                           && (typeof(DiagnosticAnalyzer).IsAssignableFrom(type)
                               || typeof(CodeFixProvider).IsAssignableFrom(type)))
            .Where(type => !type.IsSealed)
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Analyzers and code-fix providers must be sealed:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void FixableRules_HaveFixAllTests()
    {
        var failures = new List<string>();

        foreach (var rule in RuleCatalog.All.Where(rule => rule.HasCodeFix))
        {
            var testDir = Path.Combine(_repoRoot, "tests", "LinqContraband.Tests", "Analyzers", rule.Slug);
            var testFiles = Directory.Exists(testDir)
                ? Directory.GetFiles(testDir, "*.cs", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            // "BatchFixedCode" is the CodeFixTest property a test must set to verify batch (FixAll)
            // application; checking for it (rather than the string "FixAll") avoids being satisfied
            // by comments or unrelated identifiers such as NumberOfFixAllIterations.
            if (!testFiles.Any(file => File.ReadAllText(file).Contains("BatchFixedCode", StringComparison.Ordinal)))
                failures.Add($"{rule.Id}: fixer tests never exercise FixAll (batch) behaviour");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
