using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC021_AvoidIgnoreQueryFilters;

public class AvoidIgnoreQueryFiltersSafeSample
{
    public static void Run(AppDbContext db, Guid tenantReviewUserId)
    {
        Console.WriteLine("Testing LC021 safe sample...");

#pragma warning disable LC021
        // SAFE: audited break-glass query for a tenant review workflow. Keep the suppression local and justified.
        var reviewedUser = db.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .TagWith("LC021 safe sample: audited tenant review bypass")
            .Where(user => user.Id == tenantReviewUserId)
            .OrderBy(user => user.Id)
            .Take(1)
            .ToList();
#pragma warning restore LC021
    }
}
