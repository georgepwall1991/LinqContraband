using System.Globalization;
using FsCheck;
using FsCheck.Xunit;
using LinqContraband.Scan;

namespace LinqContraband.Tests.Scan;

// FsCheck generates and shrinks inputs on every normal test run. A failure reports
// the replay seed and the smallest failing input, so it can become a regression test.
public sealed class ScanOptionsFuzzTests
{
    [Property(MaxTest = 1000)]
    public void ArbitraryArguments_ReturnOptionsOrAnError(NonNull<string>[] input)
    {
        var args = input.Select(value => value.Get).ToArray();
        var success = ScanOptions.TryParse(args, out var options, out var error);

        Assert.NotNull(options);
        if (!success)
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
            return;
        }

        Assert.Null(error);
        Assert.True(options.Top >= 0);
        Assert.True(options.FindingsPerRule >= 0);
        Assert.All(options.Rules.Concat(options.SkippedRules), rule =>
            Assert.Matches("^(LC|EF)[0-9]+$", rule));
        Assert.Contains(options.FailOn, new string?[] { null, "Error", "Warning", "Info" });
    }

    [Property(MaxTest = 1000)]
    public void NonnegativeCounts_RoundTrip(NonNegativeInt top, NonNegativeInt findings)
    {
        var args = new[]
        {
            "--top", top.Get.ToString(CultureInfo.InvariantCulture),
            "--findings", findings.Get.ToString(CultureInfo.InvariantCulture)
        };

        Assert.True(ScanOptions.TryParse(args, out var options, out var error), error);
        Assert.Equal(top.Get, options.Top);
        Assert.Equal(findings.Get, options.FindingsPerRule);
    }

    [Property(MaxTest = 1000)]
    public void UnknownFlags_AreRejected(NonNull<string> suffix)
    {
        var flag = "--unknown-" + suffix.Get;
        Assert.False(ScanOptions.TryParse([flag], out _, out var error));
        Assert.Equal($"Unknown option '{flag}'.", error);
    }
}
