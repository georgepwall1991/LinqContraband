using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC056_StoredProcedureComposed;

// Not called from Program: the sample context uses the in-memory provider, which cannot run SQL.
public static class StoredProcedureComposedSample
{
    public static int CountAdults(AppDbContext db, int tenantId)
    {
        // VIOLATION: EF Core wraps the SQL in a subquery to apply Count, and SQL Server cannot
        // select from EXEC, so this throws at run time.
        return db.Database
            .SqlQuery<int>($"EXEC dbo.GetTenantUserAges {tenantId}")
            .Count(age => age >= 18);
    }

    public static int CountAdultsInMemory(AppDbContext db, int tenantId)
    {
        // CORRECT: run the procedure as it is, then count in memory.
        var ages = db.Database
            .SqlQuery<int>($"EXEC dbo.GetTenantUserAges {tenantId}")
            .ToList();

        return ages.Count(age => age >= 18);
    }
}
