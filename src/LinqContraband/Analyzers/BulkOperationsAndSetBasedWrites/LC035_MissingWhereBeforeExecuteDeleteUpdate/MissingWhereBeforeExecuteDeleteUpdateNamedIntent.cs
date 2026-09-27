using System;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

public sealed partial class MissingWhereBeforeExecuteDeleteUpdateAnalyzer
{
    private static readonly HashSet<string> ClearVerbs = new(StringComparer.Ordinal)
    {
        "Clear", "Purge", "Truncate", "Wipe"
    };

    private static readonly HashSet<string> AllVerbs = new(StringComparer.Ordinal)
    {
        "Clear", "Delete", "Remove", "Purge", "Truncate", "Wipe", "Reset"
    };

    // Nouns that name everything rather than a subset: ClearAllData, DeleteAllRows.
    private static readonly HashSet<string> WholeSetNouns = new(StringComparer.Ordinal)
    {
        "Data", "Rows", "Records", "Entries", "Items", "Entities", "Tables"
    };

    private static readonly (string Singular, string Plural)[] IrregularPlurals =
    {
        ("Person", "People"), ("Child", "Children"), ("Man", "Men"), ("Woman", "Women"),
        ("Mouse", "Mice"), ("Goose", "Geese"), ("Tooth", "Teeth"), ("Foot", "Feet")
    };

    // Classical and other listed plurals, matched on the last word. A word missing here only makes
    // LC035 report a clear it could have recognised, which is the safe direction. AlsoRegular accepts the regular form too
    // (Scarves and Scarfs, Radii and Radiuses, Media and Mediums).
    private static readonly (string Singular, string Plural, bool AlsoRegular)[] ClassicalPlurals =
    {
        ("Leaf", "Leaves", false), ("Loaf", "Loaves", false), ("Thief", "Thieves", false),
        ("Sheaf", "Sheaves", false), ("Half", "Halves", false), ("Calf", "Calves", false),
        ("Shelf", "Shelves", false), ("Wolf", "Wolves", false), ("Self", "Selves", false),
        ("Elf", "Elves", false), ("Knife", "Knives", false), ("Wife", "Wives", false),
        ("Life", "Lives", false), ("Scarf", "Scarves", true), ("Hoof", "Hooves", true),
        ("Dwarf", "Dwarves", true), ("Wharf", "Wharves", true),
        ("Radius", "Radii", true), ("Cactus", "Cacti", true), ("Fungus", "Fungi", true),
        ("Nucleus", "Nuclei", true), ("Stimulus", "Stimuli", true), ("Alumnus", "Alumni", true),
        ("Syllabus", "Syllabi", true), ("Focus", "Foci", true),
        ("Datum", "Data", false), ("Medium", "Media", true), ("Curriculum", "Curricula", true),
        ("Bacterium", "Bacteria", false), ("Stratum", "Strata", false), ("Memorandum", "Memoranda", true),
        ("Criterion", "Criteria", false), ("Phenomenon", "Phenomena", false),
        ("Quiz", "Quizzes", true), ("Whiz", "Whizzes", false)
    };

    // Words whose plural is the same word: ClearSeries over Set<Series>().
    private static readonly string[] InvariantPlurals =
    {
        "Series", "Species", "Sheep", "Fish", "Deer", "News", "Data"
    };

    // Each name also matches with an Async suffix (IsGenericEntryPoint), so ConsumeAsync counts.
    private static readonly HashSet<string> GenericEntryPointNames = new(StringComparer.Ordinal)
    {
        "Handle", "HandleAsync", "Execute", "ExecuteAsync", "Run", "RunAsync",
        "Invoke", "InvokeAsync", "Consume", "Process", "ProcessAsync", "Perform"
    };

    private static readonly string[] HandlerTypeSuffixes =
    {
        "CommandHandler", "RequestHandler", "Handler", "Command", "Consumer", "Job"
    };

    /// <summary>
    /// A method whose name says it empties the table means the whole-table bulk operation on purpose:
    /// Moonglade's <c>ClearAllData()</c>, or <c>ClearActivityLogsCommandHandler.HandleAsync</c> running
    /// <c>db.ActivityLog.ExecuteDeleteAsync()</c>. After dropping a trailing <c>Async</c>, the name must
    /// either be a clearing verb, <c>All</c> and then the table or a whole-set noun (<c>ClearAllData</c>,
    /// <c>DeleteAllUsers</c>), or a clearing verb followed by the table (<c>ClearMentions</c> over
    /// <c>MentionEntity</c>). The table is the plural of the entity or the set's property name.
    /// <c>DeleteAllInactiveUsers</c>, <c>ClearUserSession</c> over <c>Sessions</c> and <c>ClearAddress</c>
    /// over <c>Addresses</c> do not qualify.
    /// </summary>
    private static bool IsNamedWholeTableOperation(IInvocationOperation invocation)
    {
        // Only a bare table (db.Logs, db.Set<Log>()) counts; a query handed in by a caller does not.
        if (!IsBareTable(invocation.GetInvocationReceiver()))
            return false;

        var tableNames = GetTableNames(invocation).ToList();
        return GetIntentNames(invocation.Syntax).Any(name => NameSaysWholeTable(name, tableNames));
    }

    private static bool IsBareTable(IOperation? receiver)
    {
        var current = receiver?.UnwrapConversions();
        return current switch
        {
            IPropertyReferenceOperation property => property.Type.IsDbSet(),
            IFieldReferenceOperation field => field.Type.IsDbSet(),
            IInvocationOperation { TargetMethod.Name: "Set" } set => set.Arguments.Length == 0 && set.Type.IsDbSet(),
            _ => false
        };
    }

    private static bool NameSaysWholeTable(string name, List<string> tableNames)
    {
        var words = StripAsync(SplitWords(name));

        if (words.Count < 2)
            return false;

        // ClearAllData, DeleteAllSessions: the name starts with a clearing verb, then All, then the whole set.
        // DeleteAllInactiveUsers names a subset, and PreviewDeleteAllUsers or CanDeleteAllUsers do not
        // clear anything themselves; none of them count.
        if (words.Count > 2 && AllVerbs.Contains(words[0]) && words[1] == "All")
        {
            var set = string.Concat(words.Skip(2));
            if (WholeSetNouns.Contains(set) || NamesTable(set, tableNames))
                return true;
        }

        return ClearVerbs.Contains(words[0]) && NamesTable(string.Concat(words.Skip(1)), tableNames);
    }

    /// <summary>
    /// The nearest named callable decides. Only a bare one-word name such as <c>Truncate()</c> or
    /// <c>Clear()</c> says nothing about which rows, so the enclosing callable's name is read instead;
    /// <c>DeleteAllInactiveUsers()</c> inside <c>ClearUsers()</c> decides alone.
    /// </summary>
    private static IEnumerable<string> GetIntentNames(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case LocalFunctionStatementSyntax localFunction:
                {
                    var localName = localFunction.Identifier.ValueText;
                    yield return localName;
                    if (StripAsync(SplitWords(localName)).Count > 1)
                        yield break;
                    break;
                }
                case MethodDeclarationSyntax method:
                {
                    var methodName = method.Identifier.ValueText;
                    if (!IsGenericEntryPoint(methodName))
                    {
                        yield return methodName;
                        yield break;
                    }

                    if (method.Parent is not TypeDeclarationSyntax type)
                        yield break;

                    var typeName = type.Identifier.ValueText;
                    foreach (var suffix in HandlerTypeSuffixes)
                    {
                        if (typeName.Length > suffix.Length && typeName.EndsWith(suffix, StringComparison.Ordinal))
                        {
                            yield return typeName.Substring(0, typeName.Length - suffix.Length);
                            yield break;
                        }
                    }

                    yield break;
                }
                case TypeDeclarationSyntax:
                    yield break;
            }
        }
    }

    /// <summary>
    /// Names that mean the whole table: the plural of the entity (minus an <c>Entity</c> suffix), and the
    /// DbSet property name. The property name counts as is only when it differs from the entity name,
    /// since <c>DbSet&lt;Address&gt; Address</c> is singular; its plural always counts.
    /// </summary>
    private static IEnumerable<string> GetTableNames(IInvocationOperation invocation)
    {
        var entityNames = new List<string>();
        if (invocation.TargetMethod.TypeArguments.FirstOrDefault() is { } entityType)
        {
            var entityName = entityType.Name;
            entityNames.Add(entityName);
            if (entityName.Length > "Entity".Length && entityName.EndsWith("Entity", StringComparison.Ordinal))
                entityNames.Add(entityName.Substring(0, entityName.Length - "Entity".Length));
        }

        foreach (var entityName in entityNames)
        {
            foreach (var plural in Pluralize(entityName))
                yield return plural;
        }

        var current = invocation.GetInvocationReceiver()?.UnwrapConversions();
        while (current is IInvocationOperation chained && chained.GetInvocationReceiver() is { } receiver)
            current = receiver.UnwrapConversions();

        if (current is IPropertyReferenceOperation property && property.Type.IsDbSet())
        {
            var setName = property.Property.Name;
            if (!entityNames.Any(entityName => string.Equals(entityName, setName, StringComparison.OrdinalIgnoreCase)))
                yield return setName;
            foreach (var plural in Pluralize(setName))
                yield return plural;
        }
    }

    private static bool NamesTable(string words, List<string> tableNames) =>
        tableNames.Any(tableName => string.Equals(words, tableName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The accepted plural spellings of a name, matched on its last word: listed irregular, invariant
    /// and classical words (<c>Leaf</c> to <c>Leaves</c>, <c>Radius</c> to <c>Radii</c>, <c>Criterion</c>
    /// to <c>Criteria</c>), <c>-sis</c>/<c>-xis</c> to <c>-ses</c>/<c>-xes</c>, and otherwise only the
    /// regular English plural, so <c>Safe</c> becomes <c>Safes</c> and <c>Status</c> <c>Statuses</c>.
    /// </summary>
    private static List<string> Pluralize(string name)
    {
        var plurals = new List<string>();
        if (name.Length == 0)
            return plurals;

        foreach (var invariant in InvariantPlurals)
        {
            if (EndsWithWord(name, invariant))
            {
                plurals.Add(name);
                return plurals;
            }
        }

        foreach (var (singular, plural) in IrregularPlurals)
        {
            if (EndsWithWord(name, singular))
            {
                plurals.Add(name.Substring(0, name.Length - singular.Length) + plural);
                return plurals;
            }
        }

        if (name.Length > 1 && name.EndsWith("y", StringComparison.Ordinal) && !IsVowel(name[name.Length - 2]))
        {
            plurals.Add(name.Substring(0, name.Length - 1) + "ies");
            return plurals;
        }

        // Analysis becomes Analyses and Axis Axes, never Analysises. Other -is words are regular:
        // Iris becomes Irises and Trellis Trellises.
        if (name.EndsWith("sis", StringComparison.Ordinal) || name.EndsWith("xis", StringComparison.Ordinal))
        {
            plurals.Add(name.Substring(0, name.Length - 2) + "es");
            return plurals;
        }

        // Only listed words take a classical plural; Safe stays Safes and Status Statuses.
        foreach (var (singular, plural, alsoRegular) in ClassicalPlurals)
        {
            if (!EndsWithWord(name, singular))
                continue;

            plurals.Add(name.Substring(0, name.Length - singular.Length) + plural);
            if (!alsoRegular)
                return plurals;
            break;
        }

        if (name.EndsWith("s", StringComparison.Ordinal) || name.EndsWith("x", StringComparison.Ordinal) ||
            name.EndsWith("z", StringComparison.Ordinal) || name.EndsWith("ch", StringComparison.Ordinal) ||
            name.EndsWith("sh", StringComparison.Ordinal))
            plurals.Add(name + "es");
        else
            plurals.Add(name + "s");

        // Consonant + o takes either spelling: Heroes and Potatoes, but Photos and Pianos. Video stays Videos.
        if (name.Length > 1 && name.EndsWith("o", StringComparison.Ordinal) && !IsVowel(name[name.Length - 2]))
            plurals.Add(name + "es");

        return plurals;
    }

    private static bool IsGenericEntryPoint(string methodName) =>
        GenericEntryPointNames.Contains(methodName) ||
        (methodName.Length > "Async".Length && methodName.EndsWith("Async", StringComparison.Ordinal) &&
         GenericEntryPointNames.Contains(methodName.Substring(0, methodName.Length - "Async".Length)));

    private static List<string> StripAsync(List<string> words)
    {
        if (words.Count > 0 && words[words.Count - 1] == "Async")
            words.RemoveAt(words.Count - 1);
        return words;
    }

    private static bool EndsWithWord(string name, string word) =>
        name.EndsWith(word, StringComparison.Ordinal) ||
        (name.Length == word.Length && string.Equals(name, word, StringComparison.OrdinalIgnoreCase));

    private static bool IsVowel(char c) => "aeiouAEIOU".IndexOf(c) >= 0;

    private static List<string> SplitWords(string name)
    {
        var words = new List<string>();
        var start = 0;
        for (var i = 1; i <= name.Length; i++)
        {
            if (i == name.Length || (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) || name[i] == '_')
            {
                var word = name.Substring(start, i - start).Trim('_');
                if (word.Length > 0)
                    words.Add(word);
                start = i;
            }
        }

        return words;
    }
}
