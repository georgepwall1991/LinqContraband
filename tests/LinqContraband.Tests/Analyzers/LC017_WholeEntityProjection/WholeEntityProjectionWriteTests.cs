using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC017_WholeEntityProjection.WholeEntityProjectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC017_WholeEntityProjection;

/// <summary>
/// Loading tracked entities to change them and save is not a read that a projection could serve: a projected
/// anonymous type has read-only members (CS0200) and nothing to save. Writes, and entities stored where the method
/// can no longer see how they are used, keep LC017 quiet.
/// </summary>
public partial class WholeEntityProjectionTests
{
    private static string WriteProgram(string body) => Usings + @"
class Program
{
    public object Holder { get; set; }
    private List<LargeEntity> _cache;
    private DbSet<RichEntity> _riches;

    public void Run(DateTime now)
    {
        var db = new MyDbContext();
        " + body + @"
    }
}

class ViewModel { public List<LargeEntity> Items { get; set; } }

class Tag { public string Label { get; set; } }
class Owner { public string Name { get; set; } public int Visits { get; set; } }
class RichEntity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Address { get; set; }
    public string City { get; set; }
    public string Country { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public List<Tag> Tags { get; set; }
    public Owner Owner { get; set; }
    public void Archive() { }
}
" + MockNamespace;

    [Theory]
    // Load, mutate, save.
    [InlineData(@"var entities = db.LargeEntities.Where(e => e.Id > 0).ToList();
        foreach (var e in entities)
            e.UpdatedAt = now;
        db.SaveChanges();")]
    [InlineData(@"var entities = db.LargeEntities.Where(e => e.Id > 0).ToList();
        foreach (var e in entities)
            e.Quantity++;
        db.SaveChanges();")]
    [InlineData(@"var entities = db.LargeEntities.Where(e => e.Id > 0).ToList();
        foreach (var e in entities)
            e.Quantity += 1;")]
    [InlineData(@"var entities = db.LargeEntities.Where(e => e.Id > 0).ToList();
        for (var i = 0; i < entities.Count; i++)
        {
            var e = entities[i];
            e.Name = e.Name.Trim();
        }")]
    // Stored where the method can no longer see how it is used.
    [InlineData(@"var entities = db.LargeEntities.Where(e => e.Id > 0).ToList();
        _cache = entities;
        foreach (var e in entities) Console.WriteLine(e.Name);")]
    [InlineData(@"var entities = db.LargeEntities.Where(e => e.Id > 0).ToList();
        var vm = new ViewModel { Items = entities };
        foreach (var e in entities) Console.WriteLine(e.Name);")]
    [InlineData(@"var entities = db.LargeEntities.Where(e => e.Id > 0).ToList();
        foreach (var e in entities)
            Holder = e;")]
    [InlineData(@"var entities = db.LargeEntities.Where(e => e.Id > 0).ToList();
        var alias = entities;
        foreach (var e in entities) Console.WriteLine(e.Name);")]
    [InlineData(@"var entities = db.LargeEntities.Where(e => e.Id > 0).ToList();
        var all = new[] { entities };
        foreach (var e in entities) Console.WriteLine(e.Name);")]
    [InlineData(@"var entities = db.LargeEntities.Where(e => e.Id > 0).ToList();
        var pair = (entities, 1);
        foreach (var e in entities) Console.WriteLine(e.Name);")]
    // Changed through a navigation or a domain method; a projection would lose the change.
    [InlineData(@"var entities = _riches.Where(e => e.Id > 0).ToList();
        foreach (var e in entities)
            e.Tags.Add(new Tag { Label = e.Name });
        db.SaveChanges();")]
    [InlineData(@"var entities = _riches.Where(e => e.Id > 0).ToList();
        foreach (var e in entities)
        {
            if (e.Name == null) continue;
            e.Tags.Clear();
        }")]
    [InlineData(@"var entities = _riches.Where(e => e.Id > 0).ToList();
        foreach (var e in entities)
            e.Owner.Name = e.Name;
        db.SaveChanges();")]
    [InlineData(@"var entities = _riches.Where(e => e.Id > 0).ToList();
        foreach (var e in entities)
            e.Owner.Visits++;")]
    [InlineData(@"var entities = _riches.Where(e => e.Id > 0).ToList();
        foreach (var e in entities)
            e.Archive();
        db.SaveChanges();")]
    public Task WrittenOrStoredEntities_StayQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(WriteProgram(body));

    [Theory]
    // Reading a property into a local, or writing into something else, is still a read.
    [InlineData(@"var entities = {|LC017:db.LargeEntities.Where(e => e.Id > 0).ToList()|};
        var names = new List<string>();
        foreach (var e in entities)
        {
            var name = e.Name;
            names.Add(name);
        }")]
    [InlineData(@"var entities = {|LC017:db.LargeEntities.Where(e => e.Id > 0).ToList()|};
        var vm = new ViewModel();
        foreach (var e in entities)
            Console.WriteLine(e.Name);")]
    [InlineData(@"var entities = {|LC017:_riches.Where(e => e.Id > 0).ToList()|};
        foreach (var e in entities)
            Console.WriteLine(e.Name.Trim() + e.Email.ToUpperInvariant());")]
    [InlineData(@"var entities = {|LC017:_riches.Where(e => e.Id > 0).ToList()|};
        var others = new List<Tag>();
        foreach (var e in entities)
            others.Add(new Tag { Label = e.Name });
        Console.WriteLine(entities.Contains(null));")]
    public Task ReadOnlyUsage_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(WriteProgram(body));
}
