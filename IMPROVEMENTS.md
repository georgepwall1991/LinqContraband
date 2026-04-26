# LinqContraband Improvements

This file tracks planned improvements and technical debt cleanup for the LinqContraband project.

## Planned Tasks

### 1. Refactoring & Consistency
- [x] **Rename `MultipleOrderByCodeFixProvider.cs` to `MultipleOrderByFixer.cs`**
    - **Context:** Most fixers in the project follow the naming convention `*Fixer.cs` (e.g., `LocalMethodFixer.cs`). LC005 is an outlier.
    - **Action:** Rename the file to match the project standard.

### 2. New Code Fixes
- [x] **Remove unsafe Code Fix for LC014 (AvoidStringCaseConversion)**
    - **Context:** The original fixer rewrote `.ToLower()`/`.ToUpper()` comparisons to `string.Equals(..., StringComparison.OrdinalIgnoreCase)`, which is provider-sensitive and can be untranslatable in EF queries.
    - **Action:** Keep LC014 analyzer-only and document database-specific remediation through collation, normalized columns, or provider-specific APIs.

### 3. Future Considerations (Backlog)
- [x] **Implement Code Fix for LC015 (MissingOrderBy)**
    - **Idea:** Suggest adding `.OrderBy(x => x.Id)` before `Skip`/`Take`/`Last`.
- [x] **New Analyzer: `DateTime.Now` in Queries**
    - **Idea:** Flag `DateTime.Now` to encourage passing it as a variable for better query caching/testing.
