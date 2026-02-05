using LinqContraband.Sample.Data;
using LinqContraband.Sample.Samples.LC001_LocalMethod;
using LinqContraband.Sample.Samples.LC002_PrematureMaterialization;
using LinqContraband.Sample.Samples.LC003_AnyOverCount;
using LinqContraband.Sample.Samples.LC004_IQueryableLeak;
using LinqContraband.Sample.Samples.LC005_MultipleOrderBy;
using LinqContraband.Sample.Samples.LC006_CartesianExplosion;
using LinqContraband.Sample.Samples.LC007_NPlusOneLooper;
using LinqContraband.Sample.Samples.LC008_SyncBlocker;
using LinqContraband.Sample.Samples.LC009_MissingAsNoTracking;
using LinqContraband.Sample.Samples.LC010_SaveChangesInLoop;
using LinqContraband.Sample.Samples.LC011_EntityMissingPrimaryKey;
using LinqContraband.Sample.Samples.LC012_OptimizeRemoveRange;
using LinqContraband.Sample.Samples.LC014_AvoidStringCaseConversion;
using LinqContraband.Sample.Samples.LC015_MissingOrderBy;
using LinqContraband.Sample.Samples.LC018_AvoidFromSqlRawWithInterpolation;
using LinqContraband.Sample.Samples.LC020_StringContainsWithComparison;
using LinqContraband.Sample.Samples.LC021_AvoidIgnoreQueryFilters;
using LinqContraband.Sample.Samples.LC023_FindInsteadOfFirstOrDefault;
using LinqContraband.Sample.Samples.LC025_AsNoTrackingWithUpdate;
using LinqContraband.Sample.Samples.LC026_MissingCancellationToken;
using LinqContraband.Sample.Samples.LC029_RedundantIdentitySelect;
using LinqContraband.Sample.Samples.LC030_DbContextInSingleton;

namespace LinqContraband.Sample;

internal class Program
{
    private static async Task Main(string[] args)
    {
        using var db = new AppDbContext();
        var users = db.Users.AsQueryable();

        LocalMethodSample.Run(users);
        PrematureMaterializationSample.Run(users);
        AnyOverCountSample.Run(users);
        IQueryableLeakSample.Run();
        MultipleOrderBySample.Run(users);

        // LC006 - LC010
        CartesianExplosionSample.Run(users);
        NPlusOneLooperSample.Run(db, users);
        await SyncBlockerSample.RunAsync(users);
        MissingAsNoTrackingSample.Run(users);
        SaveChangesInLoopSample.Run(users);

        EntityMissingPrimaryKeySample.Run();
        OptimizeRemoveRangeSample.Run();

        // LC014: AvoidStringCaseConversion
        AvoidStringCaseConversionSample.Run(db);

        // LC015: MissingOrderBy
        MissingOrderBySample.Run(users);

        // New samples
        AvoidFromSqlRawWithInterpolationSample.Run(db);
        StringContainsWithComparisonSample.Run(db);
        AvoidIgnoreQueryFiltersSample.Run(db);
        FindInsteadOfFirstOrDefaultSample.Run(db);
        AsNoTrackingWithUpdateSample.Run(db);

        // LC026 - LC030
        await MissingCancellationTokenSample.RunAsync(db, CancellationToken.None);
        RedundantIdentitySelectSample.Run(users);
        new DbContextInSingletonSample(db).Run();
    }
}
