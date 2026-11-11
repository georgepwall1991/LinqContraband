namespace LinqContraband.CrossProject;

/// <summary>
/// The text format of the facts <see cref="CrossProjectFactExporter"/> writes into a scan build's compiler log and
/// <c>LinqContraband.Scan</c> reads back. A fact is the message of one <see cref="DiagnosticId"/> diagnostic:
/// fields joined by <see cref="Separator"/>, starting with <see cref="Prefix"/> and the fact kind. Methods are named
/// by documentation comment ID, which is the same string in every project that can see the method.
/// </summary>
internal static class CrossProjectFacts
{
    public const string DiagnosticId = "LCX001";

    /// <summary>
    /// The global analyzer config key that turns the export on. Only <c>LinqContraband.Scan</c> sets it, so a
    /// normal build never produces facts.
    /// </summary>
    public const string OptionKey = "linqcontraband_scan.cross_project_facts";

    public const string Prefix = "lcx1";
    public const char Separator = '|';

    /// <summary><c>Q|method|query</c>: the method runs this query once per call.</summary>
    public const string RunsQuery = "Q";

    /// <summary><c>S|method|save</c>: the method saves once per call.</summary>
    public const string Saves = "S";

    /// <summary>
    /// <c>C|method|kind|callee|root|memoized</c>: the method calls, once per call, a method whose body is in
    /// another project. <c>memoized</c> is 1 when the method consults a cache or remembered state first.
    /// </summary>
    public const string Calls = "C";

    /// <summary>
    /// <c>I|root|implementation|test</c>: a concrete type implements the interface method, or overrides the virtual
    /// method, <c>root</c> with <c>implementation</c> (empty when it cannot be resolved). <c>test</c> is 1 when the
    /// type is declared in a test project, whose fakes do not count as implementations.
    /// </summary>
    public const string Implements = "I";

    /// <summary>
    /// <c>L|rule|kind|callee|root|loop|operator|name|severity</c>: a call that runs once per item, reported at the
    /// call, which <c>rule</c> (7 or 10) would report if the callee ran a query or saved.
    /// </summary>
    public const string PerItemCall = "L";
}
