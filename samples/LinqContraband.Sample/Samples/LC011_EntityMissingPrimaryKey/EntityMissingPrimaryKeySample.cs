namespace LinqContraband.Sample.Samples.LC011_EntityMissingPrimaryKey;

/// <summary>
///     Demonstrates the "Entity Missing Primary Key" violation (LC011).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Defining an entity class in a <c>DbSet&lt;T&gt;</c> without a Primary Key.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> EF Core requires a Primary Key to track entity identity. Without it,
///         it cannot perform updates, deletes, or reliable change tracking. This often results in a runtime exception
///         (<c>ModelValidationException</c>) during startup or unexpected behavior.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Ensure every entity has a key defined via:
///         <list type="bullet">
///             <item>Naming convention (<c>Id</c> or <c>ClassNameId</c>)</item>
///             <item>The <c>[Key]</c> data annotation</item>
///             <item>The Fluent API (<c>HasKey</c>) in <c>OnModelCreating</c></item>
///         </list>
///     </para>
/// </remarks>
public class EntityMissingPrimaryKeySample
{
    /// <summary>
    ///     This sample is primarily static analysis on the DbContext definition.
    /// </summary>
    public static void Run()
    {
        Console.WriteLine("Testing LC011 (Design-time check, see AppDbContext.cs)...");
        // The violation is located in AppDbContext.cs on KeylessModelDbContext.Products. That context is never
        // instantiated here, because EF Core would reject its model at startup.
    }
}
