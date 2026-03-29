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
using LinqContraband.Sample.Samples.LC032_ExecuteUpdateForBulkUpdates;
using LinqContraband.Sample.Samples.LC033_UseFrozenSetForStaticMembershipCaches;
using LinqContraband.Sample.Samples.LC034_AvoidExecuteSqlRawWithInterpolation;
using LinqContraband.Sample.Samples.LC035_MissingWhereBeforeExecuteDeleteUpdate;
using LinqContraband.Sample.Samples.LC036_DbContextCapturedAcrossThreads;
using LinqContraband.Sample.Samples.LC037_RawSqlStringConstruction;
using LinqContraband.Sample.Samples.LC038_ExcessiveEagerLoading;
using LinqContraband.Sample.Samples.LC039_NestedSaveChanges;
using LinqContraband.Sample.Samples.LC040_MixedTrackingAndNoTracking;
using LinqContraband.Sample.Samples.LC041_SingleEntityScalarProjection;
using LinqContraband.Sample.Samples.LC042_MissingQueryTags;
using LinqContraband.Sample.Samples.LC043_AsyncEnumerableBuffering;
using System.Linq;

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
        MissingAsNoTrackingSample.Run();
        SaveChangesInLoopSample.Run(users.ToList());

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

        // LC026 - LC033
        await MissingCancellationTokenSample.RunAsync(db, CancellationToken.None);
        RedundantIdentitySelectSample.Run(users);
        new DbContextInSingletonSample(db).Run();
        ExecuteUpdateForBulkUpdatesSample.Run();
        UseFrozenSetForStaticMembershipCachesSample.Run();

        // LC034 - LC043
        await ExecuteSqlRawInterpolationSample.RunAsync(db);
        MissingWhereBeforeExecuteDeleteUpdateSample.Run(db);
        DbContextCapturedAcrossThreadsSample.Run(db);
        RawSqlStringConstructionSample.Run(db);
        ExcessiveEagerLoadingSample.Run(db);
        NestedSaveChangesSample.Run(db);
        MixedTrackingAndNoTrackingSample.Run(db);
        SingleEntityScalarProjectionSample.Run(db);
        MissingQueryTagsSample.Run(db);
        await AsyncEnumerableBufferingSample.RunAsync(db);
    }
}
