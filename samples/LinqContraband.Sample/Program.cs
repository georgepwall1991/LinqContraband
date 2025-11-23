using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

namespace LinqContraband.Sample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var users = new List<User>
            {
                new User { Id = Guid.NewGuid(), Name = "Alice", Age = 30, Orders = new(), Roles = new() },
                new User { Id = Guid.NewGuid(), Name = "Bob", Age = 25, Orders = new(), Roles = new() }
            }.AsQueryable();

            LocalMethodSample.Run(users);
            PrematureMaterializationSample.Run(users);
            AnyOverCountSample.Run(users);
            IQueryableLeakSample.Run();
            MultipleOrderBySample.Run(users);
            CartesianExplosionSample.Run(users);
            NPlusOneLooperSample.Run(users);
            await SyncBlockerSample.RunAsync(users);
            MissingAsNoTrackingSample.Run(users);
            SaveChangesInLoopSample.Run(users);
            EntityMissingPrimaryKeySample.Run();
            OptimizeRemoveRangeSample.Run();
        }
    }
}
