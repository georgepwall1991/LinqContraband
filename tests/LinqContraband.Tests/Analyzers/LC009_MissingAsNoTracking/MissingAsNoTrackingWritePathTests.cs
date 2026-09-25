using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer,
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingFixer>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// Write paths the analyzer must recognise (so it stays quiet), and entities that leave the method
/// (so it reports without the one-click fix): adding AsNoTracking() there can turn a save made by
/// a caller or callee into a silent no-op.
/// </summary>
public class MissingAsNoTrackingWritePathTests
{
    private const string Mocks = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public int SaveChanges() => 0;
    }

    public class DbSet<T> : System.Linq.IQueryable<T> where T : class
    {
        public System.Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => null;
        public System.Linq.IQueryProvider Provider => null;
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static System.Linq.IQueryable<T> AsNoTracking<T>(this System.Linq.IQueryable<T> source) => source;
    }
}

namespace Shop
{
    public class Line { public int Quantity { get; set; } }

    public class Order
    {
        public int Id { get; set; }
        public string Status;
        public System.Collections.Generic.List<Line> Lines { get; } = new System.Collections.Generic.List<Line>();
        public void Ship() => Status = ""Shipped"";
        public override string ToString() => Id.ToString();
    }

    public class OrderDto { public string Status { get; set; } }

    public class Basket { public System.Collections.Generic.List<Line> Lines { get; set; } = new System.Collections.Generic.List<Line>(); }

    public interface IMapper { TDest Map<TSource, TDest>(TSource source, TDest destination); }

    public class ShopContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
    }
}
";

    private const string Usings = @"using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Shop;
";

    private static string Code(string members) => Usings + @"
class Service
{
    private readonly ShopContext db = new ShopContext();
    private readonly IMapper mapper = null;
    private List<Order> cache;

" + members + @"

    void Commit() => db.SaveChanges();
    void Show(object value) { }
}
" + Mocks;

    private static Task VerifyQuietAsync(string members) =>
        VerifyFix.VerifyAnalyzerAsync(Code(members));

    // The diagnostic is reported but no fix is offered: fixed code equals the source.
    private static Task VerifyReportedWithoutFixAsync(string members) =>
        VerifyFix.VerifyCodeFixAsync(Code(members), Code(members));

    // Returned entities report only with dotnet_code_quality.LC009.report_returned_entities = true.
    private static Task VerifyReportedWithoutFixWhenOptedInAsync(string members) =>
        ReturnedEntitiesOptInVerifier.VerifyCodeFixAsync(Code(members), Code(members));

    private static Task VerifyReportedWithFixAsync(string members, string fixedMembers) =>
        VerifyFix.VerifyCodeFixAsync(Code(members), Code(fixedMembers));

    [Fact]
    public Task EntityMethodCall_IsAWritePath() => VerifyQuietAsync(@"
    void ShipFirst()
    {
        var order = db.Orders.First(o => o.Id == 1);
        order.Ship();
        Commit();
    }");

    [Fact]
    public Task InlineEntityMethodCall_IsAWritePath() => VerifyQuietAsync(@"
    void ShipFirst()
    {
        db.Orders.First(o => o.Id == 1).Ship();
        Commit();
    }");

    [Fact]
    public Task NavigationCollectionAdd_IsAWritePath() => VerifyQuietAsync(@"
    void AddLine()
    {
        var order = db.Orders.First(o => o.Id == 1);
        order.Lines.Add(new Line());
        Commit();
    }");

    [Fact]
    public Task FieldWrite_IsAWritePath() => VerifyQuietAsync(@"
    void Cancel()
    {
        var order = db.Orders.First(o => o.Id == 1);
        order.Status = ""Cancelled"";
        Commit();
    }");

    [Fact]
    public Task MapperDestination_IsAWritePath() => VerifyQuietAsync(@"
    void Update(OrderDto dto)
    {
        var order = db.Orders.First(o => o.Id == 1);
        mapper.Map(dto, order);
        Commit();
    }");

    [Fact]
    public Task CoalescedWithNewEntity_FieldWrite_IsAWritePath() => VerifyQuietAsync(@"
    void Upsert()
    {
        var order = db.Orders.FirstOrDefault(o => o.Id == 1) ?? new Order();
        order.Status = ""Open"";
        Commit();
    }");

    [Fact]
    public Task CoalescedWithNewEntity_ReadOnly_StillReports_AndKeepsTheFix() => VerifyReportedWithFixAsync(@"
    void Read()
    {
        var order = {|LC009:db.Orders.FirstOrDefault(o => o.Id == 1)|} ?? new Order();
        var text = order.ToString();
    }", @"
    void Read()
    {
        var order = db.Orders.AsNoTracking().FirstOrDefault(o => o.Id == 1) ?? new Order();
        var text = order.ToString();
    }");

    [Fact]
    public Task MapperSource_StillReports_WithoutFix() => VerifyReportedWithoutFixAsync(@"
    void Read()
    {
        var order = {|LC009:db.Orders.First(o => o.Id == 1)|};
        var dto = mapper.Map(order, new OrderDto());
    }");

    [Fact]
    public Task ToStringCall_StillReports_AndKeepsTheFix() => VerifyReportedWithFixAsync(@"
    void Read()
    {
        var order = {|LC009:db.Orders.First(o => o.Id == 1)|};
        var text = order.ToString();
    }", @"
    void Read()
    {
        var order = db.Orders.AsNoTracking().First(o => o.Id == 1);
        var text = order.ToString();
    }");

    [Fact]
    public Task ResultListAdd_StillReports_AndKeepsTheFix() => VerifyReportedWithFixAsync(@"
    int Read()
    {
        var orders = {|LC009:db.Orders.ToList()|};
        orders.Add(new Order());
        return orders.Count;
    }", @"
    int Read()
    {
        var orders = db.Orders.AsNoTracking().ToList();
        orders.Add(new Order());
        return orders.Count;
    }");

    [Fact]
    public Task ScalarLinqOverResult_KeepsTheFix() => VerifyReportedWithFixAsync(@"
    List<string> Read()
    {
        var orders = {|LC009:db.Orders.ToList()|};
        var shipped = orders.Count(o => o.Status == ""Shipped"");
        return orders.Select(o => o.Status).ToList();
    }", @"
    List<string> Read()
    {
        var orders = db.Orders.AsNoTracking().ToList();
        var shipped = orders.Count(o => o.Status == ""Shipped"");
        return orders.Select(o => o.Status).ToList();
    }");

    [Fact]
    public Task ReturnedMaterializer_ReportsWithoutFix_WhenOptedIn() => VerifyReportedWithoutFixWhenOptedInAsync(@"
    List<Order> Read() => {|LC009:db.Orders.Where(o => o.Id > 0).ToList()|};");

    [Fact]
    public Task ReturnedResultLocal_ReportsWithoutFix_WhenOptedIn() => VerifyReportedWithoutFixWhenOptedInAsync(@"
    Order Read()
    {
        var order = {|LC009:db.Orders.First(o => o.Id == 1)|};
        return order;
    }");

    [Fact]
    public Task ReturnedFilteredCopy_ReportsWithoutFix_WhenOptedIn() => VerifyReportedWithoutFixWhenOptedInAsync(@"
    List<Order> Read()
    {
        var orders = {|LC009:db.Orders.ToList()|};
        return orders.Where(o => o.Id > 1).ToList();
    }");

    [Fact]
    public Task PassedAsArgument_ReportsWithoutFix() => VerifyReportedWithoutFixAsync(@"
    void Read()
    {
        var orders = {|LC009:db.Orders.ToList()|};
        Show(orders);
    }");

    [Fact]
    public Task ForeachVariablePassedAsArgument_ReportsWithoutFix() => VerifyReportedWithoutFixAsync(@"
    void Read()
    {
        foreach (var order in {|LC009:db.Orders.ToList()|})
            Show(order);
    }");

    [Fact]
    public Task StoredInField_ReportsWithoutFix() => VerifyReportedWithoutFixAsync(@"
    void Load()
    {
        var orders = {|LC009:db.Orders.ToList()|};
        cache = orders;
    }");

    [Fact]
    public Task HandedToDelegate_ReportsWithoutFix() => VerifyReportedWithoutFixAsync(@"
    void Read()
    {
        var orders = {|LC009:db.Orders.ToList()|};
        orders.ForEach(o => Show(o));
    }");

    [Fact]
    public Task StoredInObjectInitializer_ReportsWithoutFix() => VerifyReportedWithoutFixAsync(@"
    object Read()
    {
        var order = {|LC009:db.Orders.First(o => o.Id == 1)|};
        return new { Order = order };
    }");

    // Kavita: entities reached through a navigation and put into another object can join a tracked graph,
    // where untracked duplicates of one key fail to attach.
    [Fact]
    public Task NavigationStoredInAnotherObject_ReportsWithoutFix() => VerifyReportedWithoutFixAsync(@"
    void Build()
    {
        var order = {|LC009:db.Orders.First(o => o.Id == 1)|};
        var basket = new Basket { Lines = order.Lines.Where(l => l.Quantity > 0).ToList() };
        Show(basket);
    }");

    [Fact]
    public Task NavigationPassedAsArgument_ReportsWithoutFix() => VerifyReportedWithoutFixAsync(@"
    void Read()
    {
        var order = {|LC009:db.Orders.First(o => o.Id == 1)|};
        Show(order.Lines);
    }");

    [Fact]
    public Task NavigationElementStoredFromLoop_ReportsWithoutFix() => VerifyReportedWithoutFixAsync(@"
    void Build(Basket basket)
    {
        var order = {|LC009:db.Orders.First(o => o.Id == 1)|};
        foreach (var line in order.Lines)
            basket.Lines.Add(line);
    }");

    [Fact]
    public Task ScalarReadsThroughNavigation_KeepTheFix() => VerifyReportedWithFixAsync(@"
    int Read()
    {
        var order = {|LC009:db.Orders.First(o => o.Id == 1)|};
        var total = 0;
        foreach (var line in order.Lines)
            total += line.Quantity;
        return total + order.Lines.Sum(l => l.Quantity);
    }", @"
    int Read()
    {
        var order = db.Orders.AsNoTracking().First(o => o.Id == 1);
        var total = 0;
        foreach (var line in order.Lines)
            total += line.Quantity;
        return total + order.Lines.Sum(l => l.Quantity);
    }");
}
