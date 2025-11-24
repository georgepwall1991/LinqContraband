# LinqContraband Improvements

This file tracks planned improvements and technical debt cleanup for the LinqContraband project.

## Planned Tasks

### 1. Refactoring & Consistency
- [x] **Rename `MultipleOrderByCodeFixProvider.cs` to `MultipleOrderByFixer.cs`**
    - **Context:** Most fixers in the project follow the naming convention `*Fixer.cs` (e.g., `LocalMethodFixer.cs`). LC005 is an outlier.
    - **Action:** Rename the file to match the project standard.

### 2. New Code Fixes
- [x] **Implement Code Fix for LC014 (AvoidStringCaseConversion)**
    - **Context:** The analyzer flags usage of `.ToLower()`/`.ToUpper()` in LINQ predicates, but doesn't offer an automatic fix.
    - **Action:** Create a Code Fix Provider that replaces:
        - `x.Name.ToLower() == "value"` -> `string.Equals(x.Name, "value", StringComparison.OrdinalIgnoreCase)`
        - `x.Name.ToLower() != "value"` -> `!string.Equals(x.Name, "value", StringComparison.OrdinalIgnoreCase)`
    - **Files:**
        - Create `src/LinqContraband/Analyzers/LC014_AvoidStringCaseConversion/AvoidStringCaseConversionFixer.cs`
        - Create `tests/LinqContraband.Tests/Analyzers/LC014_AvoidStringCaseConversion/AvoidStringCaseConversionFixerTests.cs`

### 3. Future Considerations (Backlog)
- [ ] **Implement Code Fix for LC015 (MissingOrderBy)**
    - **Idea:** Suggest adding `.OrderBy(x => x.Id)` before `Skip`/`Take`/`Last`.
- [ ] **New Analyzer: `DateTime.Now` in Queries**
    - **Idea:** Flag `DateTime.Now` to encourage passing it as a variable for better query caching/testing.
