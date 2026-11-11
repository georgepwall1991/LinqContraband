using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NPlusOneBenchmark.External;

namespace NPlusOneBenchmark.Corpus;

// A save or bulk command per element.

[NPlusOne(Kind.Save, Via.Direct)]
public static class W01_AddAndSaveInForeach
{
    public static void Run(ShopDb db)
    {
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
        {
            db.AuditEntries.Add(new AuditEntry { Message = $"seen {id}" });
            db.SaveChanges();
        }
    }
}

[NPlusOne(Kind.Save, Via.Direct)]
public static class W02_UpdateAndSaveInForLoop
{
    public static void Run(ShopDb db)
    {
        var products = db.Products.ToList();
        for (var i = 0; i < products.Count; i++)
        {
            products[i].Stock--;
            db.SaveChanges();
        }
    }
}

[NPlusOne(Kind.Save, Via.Direct)]
public static class W03_SaveChangesAsyncInForeach
{
    public static async Task Run(ShopDb db)
    {
        var orders = await db.Orders.Where(o => o.Status == OrderStatus.Open).ToListAsync();
        foreach (var order in orders)
        {
            order.Status = OrderStatus.Paid;
            await db.SaveChangesAsync();
        }
    }
}

[NPlusOne(Kind.Save, Via.Lambda)]
public static class W04_SaveInListForEachLambda
{
    public static void Run(ShopDb db)
    {
        var products = db.Products.ToList();
        products.ForEach(product =>
        {
            product.Stock++;
            db.SaveChanges();
        });
    }
}

[NPlusOne(Kind.Save, Via.Helper)]
public static class W05_HelperThatSaves
{
    public static void Run(ShopDb db)
    {
        foreach (var order in db.Orders.ToList())
            MarkShipped(db, order);
    }

    private static void MarkShipped(ShopDb db, Order order)
    {
        order.Status = OrderStatus.Shipped;
        db.SaveChanges();
    }
}

[NPlusOne(Kind.Save, Via.Helper)]
public static class W06_SealedRepositoryThatSaves
{
    public static void Run(ShopDb db)
    {
        var repository = new SealedOrderRepository(db);
        foreach (var id in db.Orders.Select(o => o.Id).ToList())
            repository.Record($"order {id}");
    }
}

[NPlusOne(Kind.Save, Via.Interface)]
public static class W07_UnitOfWorkCommitPerElement
{
    public static void Run(ShopDb db)
    {
        IUnitOfWork unitOfWork = new UnitOfWork(db);
        foreach (var customer in db.Customers.ToList())
        {
            customer.IsActive = !customer.IsActive;
            unitOfWork.Commit();
        }
    }
}

[NPlusOne(Kind.Save, Via.Virtual)]
public static class W08_GenericRepositoryAddSaves
{
    public static void Run(ShopDb db)
    {
        var audit = new EfRepository<AuditEntry>(db);
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            audit.Add(new AuditEntry { Message = $"customer {id}" });
    }
}

[NPlusOne(Kind.Save, Via.Direct)]
public static class W09_ExecuteSqlPerElement
{
    public static void Run(ShopDb db)
    {
        foreach (var id in db.Products.Select(p => p.Id).ToList())
            db.Database.ExecuteSql($"UPDATE Products SET Stock = Stock - 1 WHERE Id = {id}");
    }
}

[NPlusOne(Kind.Save, Via.Direct)]
public static class W10_ExecuteUpdatePerElement
{
    public static void Run(ShopDb db)
    {
        foreach (var id in db.Products.Select(p => p.Id).ToList())
            db.Products.Where(p => p.Id == id).ExecuteUpdate(s => s.SetProperty(p => p.Stock, p => p.Stock + 1));
    }
}

[NPlusOne(Kind.Save, Via.Direct)]
public static class W11_ExecuteDeletePerElement
{
    public static void Run(ShopDb db)
    {
        foreach (var id in db.OrderLines.Select(l => l.Id).ToList())
            db.OrderLines.Where(l => l.Id == id).ExecuteDelete();
    }
}

[NPlusOne(Kind.Save, Via.CrossProject)]
public static class W12_ExternalRepositoryAddAndSave
{
    public static void Run(ShopDb db)
    {
        var audit = new ExternalRepository<AuditEntry>(db);
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            audit.AddAndSave(new AuditEntry { Message = $"customer {id}" });
    }
}

[NPlusOne(Kind.Save, Via.Mediator)]
public static class W13_MediatorCommandPerElement
{
    public static int Run(ShopDb db)
    {
        var mediator = new Mediator(db);
        var written = 0;
        foreach (var id in db.Customers.Select(c => c.Id).ToList())
            written += mediator.Send<RecordAudit, int>(new RecordAudit($"customer {id}"));

        return written;
    }
}

[NPlusOne(Kind.Save, Via.Lambda)]
public static class W14_SaveInSelectLambda
{
    public static List<int> Run(ShopDb db)
    {
        var products = db.Products.ToList();
        return products.Select(product =>
        {
            product.Stock += 5;
            return db.SaveChanges();
        }).ToList();
    }
}

[NPlusOne(Kind.Save, Via.Direct)]
public static class W15_WhileQueueSaveEach
{
    public static void Run(ShopDb db)
    {
        var pending = new Queue<Order>(db.Orders.ToList());
        while (pending.Count > 0)
        {
            var order = pending.Dequeue();
            order.Status = OrderStatus.Shipped;
            db.SaveChanges();
        }
    }
}

[NPlusOne(Kind.Save, Via.Interface)]
public static class W16_GenericInterfaceRepositoryAdd
{
    public static void Run(ShopDb db)
    {
        IRepository<AuditEntry> audit = new EfRepository<AuditEntry>(db);
        foreach (var order in db.Orders.AsNoTracking().ToList())
            audit.Add(new AuditEntry { Message = $"order {order.Id}" });
    }
}

[NPlusOne(Kind.Save, Via.Direct)]
public static class W17_RemoveAndSaveInForeach
{
    public static void Run(ShopDb db)
    {
        foreach (var line in db.OrderLines.ToList())
        {
            db.OrderLines.Remove(line);
            db.SaveChanges();
        }
    }
}
