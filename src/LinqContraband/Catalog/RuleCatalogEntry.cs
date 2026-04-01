using System;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Catalog;

public sealed class RuleCatalogEntry
{
    public RuleCatalogEntry(
        string id,
        string slug,
        string title,
        string category,
        string domain,
        DiagnosticSeverity severity,
        string analyzerTypeName,
        string? fixerTypeName,
        string documentationPath,
        string samplePath,
        string analyzerSourcePath,
        bool hasCodeFix,
        string? noCodeFixRationale)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Rule id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Rule slug is required.", nameof(slug));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Rule title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Rule category is required.", nameof(category));
        if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("Rule domain is required.", nameof(domain));
        if (string.IsNullOrWhiteSpace(analyzerTypeName)) throw new ArgumentException("Analyzer type name is required.", nameof(analyzerTypeName));
        if (string.IsNullOrWhiteSpace(documentationPath)) throw new ArgumentException("Documentation path is required.", nameof(documentationPath));
        if (string.IsNullOrWhiteSpace(samplePath)) throw new ArgumentException("Sample path is required.", nameof(samplePath));
        if (string.IsNullOrWhiteSpace(analyzerSourcePath)) throw new ArgumentException("Analyzer source path is required.", nameof(analyzerSourcePath));

        if (hasCodeFix)
        {
            if (string.IsNullOrWhiteSpace(fixerTypeName))
                throw new ArgumentException("Fixer type name is required for fixable rules.", nameof(fixerTypeName));

            if (!string.IsNullOrWhiteSpace(noCodeFixRationale))
                throw new ArgumentException("Fixable rules cannot declare a no-code-fix rationale.", nameof(noCodeFixRationale));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(fixerTypeName))
                throw new ArgumentException("Non-fixable rules cannot declare a fixer type.", nameof(fixerTypeName));

            if (string.IsNullOrWhiteSpace(noCodeFixRationale))
                throw new ArgumentException("Non-fixable rules must declare a rationale.", nameof(noCodeFixRationale));
        }

        Id = id;
        Slug = slug;
        Title = title;
        Category = category;
        Domain = domain;
        Severity = severity;
        AnalyzerTypeName = analyzerTypeName;
        FixerTypeName = fixerTypeName;
        DocumentationPath = documentationPath;
        SamplePath = samplePath;
        AnalyzerSourcePath = analyzerSourcePath;
        HasCodeFix = hasCodeFix;
        NoCodeFixRationale = noCodeFixRationale;
    }

    public string Id { get; }
    public string Slug { get; }
    public string Title { get; }
    public string Category { get; }
    public string Domain { get; }
    public DiagnosticSeverity Severity { get; }
    public string AnalyzerTypeName { get; }
    public string? FixerTypeName { get; }
    public string DocumentationPath { get; }
    public string SamplePath { get; }
    public string AnalyzerSourcePath { get; }
    public bool HasCodeFix { get; }
    public string? NoCodeFixRationale { get; }
}
