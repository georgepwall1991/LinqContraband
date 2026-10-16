namespace LinqContraband.Tests.Analyzers.LC048_LostUpdateRisk;

/// <summary>
/// Leftover 5.8.0 LC048 <c>IsShapePreservingQueryMethod</c> and attribute-hierarchy arms
/// the original corpus does not isolate. <c>KnownShapePreservingQueryOperatorsReport</c>
/// only walks <c>Where</c> then <c>OrderBy</c>. Removing one leftover operator name
/// from the switch hides only that pin. <c>ConcurrencyAttributesDoNotReport</c> places
/// <c>[Timestamp]</c> on the same type; the inherited-base pin is the only case that
/// fails if the <c>BaseType</c> walk is dropped.
/// </summary>
public sealed partial class LostUpdateRiskAnalyzerTests
{
    [Fact]
    public Task OrderByDescending_ThenFirst_Reports() =>
        VerifyQueryOperatorAsync(".OrderByDescending(x => x.Id)");

    [Fact]
    public Task ThenBy_ThenFirst_Reports() =>
        VerifyQueryOperatorAsync(".OrderBy(x => x.Id).ThenBy(x => x.Quantity)");

    [Fact]
    public Task ThenByDescending_ThenFirst_Reports() =>
        VerifyQueryOperatorAsync(".OrderBy(x => x.Id).ThenByDescending(x => x.Quantity)");

    [Fact]
    public Task Skip_ThenFirst_Reports() => VerifyQueryOperatorAsync(".Skip(1)");

    [Fact]
    public Task Take_ThenFirst_Reports() => VerifyQueryOperatorAsync(".Take(1)");

    [Fact]
    public Task Distinct_ThenFirst_Reports() => VerifyQueryOperatorAsync(".Distinct()");

    [Fact]
    public Task Reverse_ThenFirst_Reports() => VerifyQueryOperatorAsync(".Reverse()");

    [Fact]
    public Task AsQueryable_ThenFirst_Reports() => VerifyQueryOperatorAsync(".AsQueryable()");

    [Fact]
    public Task Include_ThenFirst_Reports() => VerifyQueryOperatorAsync(".Include(o => o.Parent)");

    [Fact]
    public Task ThenInclude_ThenFirst_Reports() =>
        // ThenInclude requires Include on the same chain. Dropping ThenInclude fails only
        // this pin; dropping Include also fails Include_ThenFirst_Reports.
        VerifyQueryOperatorAsync(".Include(o => o.Parent).ThenInclude(p => p.Parent)");

    [Fact]
    public Task TagWith_ThenFirst_Reports() => VerifyQueryOperatorAsync(".TagWith(\"orders\")");

    [Fact]
    public Task TagWithCallSite_ThenFirst_Reports() =>
        VerifyQueryOperatorAsync(".TagWithCallSite()");

    [Fact]
    public Task IgnoreQueryFilters_ThenFirst_Reports() =>
        VerifyQueryOperatorAsync(".IgnoreQueryFilters()");

    [Fact]
    public Task IgnoreAutoIncludes_ThenFirst_Reports() =>
        VerifyQueryOperatorAsync(".IgnoreAutoIncludes()");

    [Fact]
    public Task AsSplitQuery_ThenFirst_Reports() =>
        VerifyQueryOperatorAsync(".AsSplitQuery()");

    [Fact]
    public Task AsSingleQuery_ThenFirst_Reports() =>
        VerifyQueryOperatorAsync(".AsSingleQuery()");

    [Fact]
    public Task FromSqlRaw_ThenFirst_Reports() =>
        VerifyQueryOperatorAsync(".FromSqlRaw(\"SELECT * FROM Orders\")");

    [Fact]
    public Task FromSql_ThenFirst_Reports() =>
        VerifyQueryOperatorAsync(".FromSql($\"SELECT * FROM Orders\")");

    [Fact]
    public Task FromSqlInterpolated_ThenFirst_Reports() =>
        VerifyQueryOperatorAsync(".FromSqlInterpolated($\"SELECT * FROM Orders\")");

    [Fact]
    public async Task InheritedTimestampOnBaseProperty_StaysQuiet()
    {
        await VerifyAsync(
            """
namespace Test
{
    using Microsoft.EntityFrameworkCore;
    using System.ComponentModel.DataAnnotations;

    public class VersionedOrderBase
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        [Timestamp] public byte[] Version { get; set; }
    }

    public sealed class DerivedOrder : VersionedOrderBase
    {
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<DerivedOrder> Derived { get; set; }
    }

    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            var order = db.Derived.First();
            order.Quantity++;
            db.SaveChanges();
        }
    }
}
"""
        );
    }

    private static Task VerifyQueryOperatorAsync(string chain) =>
        VerifyAsync(
            Domain
                + $$"""
    public sealed class Service
    {
        public void Update(AppDbContext db)
        {
            var order = db.Orders{{chain}}.First();
            {|LC048:order.Quantity|}++;
            db.SaveChanges();
        }
    }
}
"""
        );
}
