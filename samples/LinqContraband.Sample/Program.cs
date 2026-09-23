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
using LinqContraband.Sample.Samples.LC013_DisposedContextQuery;
using LinqContraband.Sample.Samples.LC014_AvoidStringCaseConversion;
using LinqContraband.Sample.Samples.LC015_MissingOrderBy;
using LinqContraband.Sample.Samples.LC016_AvoidDateTimeNow;
using LinqContraband.Sample.Samples.LC017_WholeEntityProjection;
using LinqContraband.Sample.Samples.LC018_AvoidFromSqlRawWithInterpolation;
using LinqContraband.Sample.Samples.LC019_ConditionalInclude;
using LinqContraband.Sample.Samples.LC020_StringContainsWithComparison;
using LinqContraband.Sample.Samples.LC021_AvoidIgnoreQueryFilters;
using LinqContraband.Sample.Samples.LC022_ToListInSelectProjection;
using LinqContraband.Sample.Samples.LC023_FindInsteadOfFirstOrDefault;
using LinqContraband.Sample.Samples.LC024_GroupByNonTranslatable;
using LinqContraband.Sample.Samples.LC025_AsNoTrackingWithUpdate;
using LinqContraband.Sample.Samples.LC026_MissingCancellationToken;
using LinqContraband.Sample.Samples.LC027_MissingExplicitForeignKey;
using LinqContraband.Sample.Samples.LC028_DeepThenInclude;
using LinqContraband.Sample.Samples.LC029_RedundantIdentitySelect;
using LinqContraband.Sample.Samples.LC030_DbContextInSingleton;
using LinqContraband.Sample.Samples.LC031_UnboundedQueryMaterialization;
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
using LinqContraband.Sample.Samples.LC044_AsNoTrackingThenModify;
using LinqContraband.Sample.Samples.LC045_MissingInclude;
using LinqContraband.Sample.Samples.LC046_ConcurrentDbContextOperations;
using LinqContraband.Sample.Samples.LC047_ExecuteDeleteBypassesTrackedDelete;
using LinqContraband.Sample.Samples.LC048_LostUpdateRisk;
using LinqContraband.Sample.Samples.LC049_IncludeIgnoredByProjection;
using LinqContraband.Sample.Samples.LC050_OrderByBeforeDistinct;
using LinqContraband.Sample.Samples.LC051_ToAsyncEnumerableOnQuery;
using LinqContraband.Sample.Samples.LC052_NonDeterministicModelData;
using LinqContraband.Sample.Samples.LC053_OverwrittenQueryFilter;
using LinqContraband.Sample.Samples.LC054_MigrateInsideTransaction;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace LinqContraband.Sample;

internal class Program
{
    private static int _failures;

    private static async Task Main(string[] args)
    {
        using var db = new AppDbContext();
        Seed(db);
        var users = db.Users.AsQueryable();

        // Each sample runs on its own: many of them demonstrate code that EF Core rejects at runtime,
        // and one sample's exception must not stop the rest from running.
        Run("LC001", () => LocalMethodSample.Run(users));
        Run("LC002", () => PrematureMaterializationSample.Run(users));
        Run("LC003", () => AnyOverCountSample.Run(users));
        Run("LC004", IQueryableLeakSample.Run);
        Run("LC005", () => MultipleOrderBySample.Run(users));

        // LC006 - LC010
        Run("LC006", () => CartesianExplosionSample.Run(users));
        Run("LC007", () => NPlusOneLooperSample.Run(db, users));
        await RunAsync("LC008", () => SyncBlockerSample.RunAsync(users));
        Run("LC009", MissingAsNoTrackingSample.Run);
        await RunAsync("LC010", async () => SaveChangesInLoopSample.Run(await users.OrderBy(user => user.Id).Take(100).ToListAsync()));

        Run("LC011", EntityMissingPrimaryKeySample.Run);
        Run("LC012", OptimizeRemoveRangeSample.Run);
        Run("LC013", QueryDisposedContext);

        // LC014: AvoidStringCaseConversion
        Run("LC014", () => AvoidStringCaseConversionSample.Run(db));

        // LC015: MissingOrderBy
        Run("LC015", () => MissingOrderBySample.Run(db.Users));
        Run("LC016", () => new AvoidDateTimeNowSample(db).Run());
        Run("LC017", () => WholeEntityProjectionSample.Run(db));

        // New samples
        Run("LC018", () => AvoidFromSqlRawWithInterpolationSample.Run(db));
        Run("LC019", () => ConditionalIncludeSample.Run(users, includeOrders: true));
        Run("LC020", () => StringContainsWithComparisonSample.Run(db));
        Run("LC021", () => AvoidIgnoreQueryFiltersSample.Run(db));
        Run("LC022", () => ToListInSelectProjectionSample.Run(users));
        Run("LC023", () => FindInsteadOfFirstOrDefaultSample.Run(db));
        Run("LC024", () => GroupByNonTranslatableSample.Run(db.Set<Order>()));
        Run("LC025", () => AsNoTrackingWithUpdateSample.Run(db));

        // LC026 - LC033
        await RunAsync("LC026", () => MissingCancellationTokenSample.RunAsync(db, CancellationToken.None));
        Run("LC027", MissingExplicitForeignKeySample.Run);
        Run("LC028", () => DeepThenIncludeSample.Run(db.Customers));
        Run("LC029", () => RedundantIdentitySelectSample.Run(users));
        Run("LC030", () => new DbContextInSingletonSample(db).Run());
        Run("LC031", () => UnboundedQueryMaterializationSample.Run(db));
        Run("LC032", ExecuteUpdateForBulkUpdatesSample.Run);
        Run("LC033", UseFrozenSetForStaticMembershipCachesSample.Run);

        // LC034 - LC044
        await RunAsync("LC034", () => ExecuteSqlRawInterpolationSample.RunAsync(db));
        Run("LC035", () => MissingWhereBeforeExecuteDeleteUpdateSample.Run(db));
        Run("LC036", () => DbContextCapturedAcrossThreadsSample.Run(db));
        Run("LC037", () => RawSqlStringConstructionSample.Run(db));
        Run("LC038", () => ExcessiveEagerLoadingSample.Run(db));
        Run("LC039", () => NestedSaveChangesSample.Run(db));
        Run("LC040", () => MixedTrackingAndNoTrackingSample.Run(db));
        Run("LC041", () => SingleEntityScalarProjectionSample.Run(db));
        Run("LC042", () => MissingQueryTagsSample.Run(db));
        await RunAsync("LC043", () => AsyncEnumerableBufferingSample.RunAsync(db));
        Run("LC044", () => AsNoTrackingThenModifySample.Run(db));
        Run("LC045", () => MissingIncludeSample.Run(db));
        await RunAsync("LC046", () => ConcurrentDbContextOperationsSample.RunAsync(db, CancellationToken.None));
        Run("LC047", ExecuteDeleteBypassesTrackedDeleteSample.Run);
        Run("LC048", UpdateOrderQuantity);
        Run("LC049", () => IncludeIgnoredByProjectionSample.Run(db));
        Run("LC050", () => OrderByBeforeDistinctSample.Run(db));
        await RunAsync("LC051", () => ToAsyncEnumerableOnQuerySample.RunAsync(db));
        Run("LC052", NonDeterministicModelDataSample.Run);
        Run("LC053", OverwrittenQueryFilterSample.Run);

        // LC054 opens a transaction around a migration, so it runs last. LC055 is a model
        // configuration check with nothing to run, and LC056 needs SQL Server stored procedures.
        await RunAsync("LC054", () => MigrateInsideTransactionSample.DemonstrateViolationAsync(db, CancellationToken.None));

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "All samples ran without throwing."
            : $"{_failures} sample(s) threw at runtime. Each one is a failure the matching rule reports at build time.");
    }

    // Seeds a few rows so the samples run their queries against real data.
    private static void Seed(AppDbContext db)
    {
        db.Database.EnsureCreated();

        db.Users.AddRange(
            new User { Id = Guid.NewGuid(), Name = "Ada", Age = 36 },
            new User { Id = Guid.NewGuid(), Name = "Grace", Age = 45 },
            new User { Id = Guid.NewGuid(), Name = "Linus", Age = 28 });

        var earth = new Planet { Name = "Earth" };
        var europe = new Continent { Name = "Europe", Planet = earth };
        var north = new ShippingRegion { Name = "North", Continent = europe };
        var norway = new ShippingCountry { Name = "Norway", ShippingRegion = north };

        db.Customers.AddRange(
            new Customer { Name = "Contoso", ShippingAddress = new ShippingAddress { Street = "1 Harbour Road", ShippingCountry = norway } },
            new Customer { Name = "Fabrikam", ShippingAddress = new ShippingAddress { Street = "2 Fjord Lane", ShippingCountry = norway } });

        db.SaveChanges();
        db.ChangeTracker.Clear();
    }

    private static void QueryDisposedContext()
    {
        _ = new DisposedContextQuerySample().GetUsers_Violation().ToList();
    }

    private static void UpdateOrderQuantity()
    {
        using var db = new LostUpdateDbContext();
        db.Orders.Add(new LostUpdateOrder { Id = 1, Quantity = 1 });
        db.SaveChanges();
        LostUpdateRiskSample.DemonstrateViolation(db);
    }

    private static void Run(string ruleId, Action sample)
    {
        try
        {
            sample();
        }
        catch (Exception ex)
        {
            Report(ruleId, ex);
        }
    }

    private static async Task RunAsync(string ruleId, Func<Task> sample)
    {
        try
        {
            await sample();
        }
        catch (Exception ex)
        {
            Report(ruleId, ex);
        }
    }

    private static void Report(string ruleId, Exception ex)
    {
        _failures++;
        if (ex is AggregateException { InnerException: { } inner })
            ex = inner;

        var message = string.Join(' ', ex.Message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (message.Length > 200)
            message = message[..200] + "...";

        Console.WriteLine($"  {ruleId} threw {ex.GetType().Name}: {message}");
    }
}
