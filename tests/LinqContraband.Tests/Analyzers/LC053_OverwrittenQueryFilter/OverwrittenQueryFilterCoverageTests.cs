using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC053_OverwrittenQueryFilter.OverwrittenQueryFilterAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC053_OverwrittenQueryFilter;

/// <summary>
/// Leftover 5.10.0 arms the original 11 analyzer cases do not isolate.
/// Dropping the <c>SwitchStatementSyntax</c> exclusive-branch arm fails only
/// the switch-section quiet pin; the existing <c>if</c>/<c>?:</c> fixture stays
/// green. Dropping the <c>SwitchExpressionSyntax</c> arm fails only the
/// switch-expression quiet pin. Two unnamed filters in the same switch
/// section, and a filter after the switch, still report so a later edit that
/// treats any switch in the member as exclusive is visible.
/// </summary>
public partial class OverwrittenQueryFilterTests
{
    [Fact]
    public async Task SwitchStatement_ExclusiveSections_StayQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            switch (_tenantId)
            {
                case 1:
                    modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == 1);
                    break;
                default:
                    modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId);
                    break;
            }"));
    }

    [Fact]
    public async Task SwitchExpression_ExclusiveArms_StayQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            _ = _tenantId switch
            {
                1 => modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == 1),
                _ => modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId)
            };"));
    }

    [Fact]
    public async Task SwitchStatement_SameSection_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            switch (_tenantId)
            {
                case 1:
                    modelBuilder.Entity<Blog>().{|#0:HasQueryFilter|}(b => b.TenantId == 1);
                    modelBuilder.Entity<Blog>().{|#1:HasQueryFilter|}(b => !b.IsDeleted);
                    break;
            }"),
            Local().WithLocation(0).WithLocation(1).WithArguments("Blog", Overwritten(2)),
            Local().WithLocation(1).WithLocation(0).WithArguments("Blog", Overwritten(2)));
    }

    [Fact]
    public async Task SwitchStatement_ThenSequentialFilter_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            switch (_tenantId)
            {
                case 1:
                    modelBuilder.Entity<Blog>().{|#0:HasQueryFilter|}(b => b.TenantId == 1);
                    break;
            }
            modelBuilder.Entity<Blog>().{|#1:HasQueryFilter|}(b => !b.IsDeleted);"),
            Local().WithLocation(0).WithLocation(1).WithArguments("Blog", Overwritten(2)),
            Local().WithLocation(1).WithLocation(0).WithArguments("Blog", Overwritten(2)));
    }
}
