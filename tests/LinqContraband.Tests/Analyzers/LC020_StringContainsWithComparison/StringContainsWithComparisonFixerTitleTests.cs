using LinqContraband.Analyzers.LC020_StringContainsWithComparison;
using LinqContraband.Tests.Extensions;

namespace LinqContraband.Tests.Analyzers.LC020_StringContainsWithComparison;

// Removing an ignore-case comparison hands case sensitivity to the database collation: SQLite matched
// 1 row before the fix and 0 after. The fix title says so whenever that can happen.
public class StringContainsWithComparisonFixerTitleTests
{
    private static string Code(string comparison) => @"
using System;
using System.Linq;

public class TestClass
{
    public object Run(IQueryable<string> query, StringComparison comparison)
    {
        return query.Where(x => x.Contains(""ann"", " + comparison + @")).ToList();
    }
}";

    [Theory]
    [InlineData("StringComparison.OrdinalIgnoreCase")]
    [InlineData("StringComparison.CurrentCultureIgnoreCase")]
    [InlineData("comparison")]
    public async Task IgnoreCaseOrUnknownComparison_TitleNamesCollation(string comparison)
    {
        var titles = await CodeActionTitles.GetAsync(
            new StringContainsWithComparisonAnalyzer(),
            new StringContainsWithComparisonFixer(),
            Code(comparison));

        Assert.Equal(new[] { "Remove StringComparison argument (database collation decides case sensitivity)" }, titles);
    }

    [Fact]
    public async Task OrdinalComparison_KeepsPlainTitle()
    {
        var titles = await CodeActionTitles.GetAsync(
            new StringContainsWithComparisonAnalyzer(),
            new StringContainsWithComparisonFixer(),
            Code("StringComparison.Ordinal"));

        Assert.Equal(new[] { "Remove StringComparison argument" }, titles);
    }
}
