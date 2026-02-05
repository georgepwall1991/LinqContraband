namespace LinqContraband.Sample.Samples.LC027_MissingExplicitForeignKey;

/// <summary>
///     Demonstrates the "Missing Explicit Foreign Key Property" violation (LC027).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Defining a reference navigation property (e.g., <c>public Customer Customer</c>)
///         without a corresponding FK property (e.g., <c>public int CustomerId</c>).
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> EF Core creates "shadow properties" for missing FKs. Shadow FKs mean
///         you can't set the FK without loading the navigation entity, make API serialization harder, and can
///         cause subtle performance issues.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Add an explicit FK property (e.g., <c>CustomerId</c>) next to the navigation.
///         See the <c>Customer</c> entity in <c>AppDbContext.cs</c> for the violation.
///     </para>
/// </remarks>
public class MissingExplicitForeignKeySample
{
    /// <summary>
    ///     This sample is primarily a static analysis check on entity definitions.
    /// </summary>
    public static void Run()
    {
        Console.WriteLine("Testing LC027 (Design-time check, see AppDbContext.cs)...");
        // The violation is located in AppDbContext.cs on the Customer entity's
        // ShippingAddress navigation property (missing ShippingAddressId FK).
    }
}
