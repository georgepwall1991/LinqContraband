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

    private static readonly HashSet<string> GenericEntryPointNames = new(StringComparer.Ordinal)
    {
        "Handle", "HandleAsync", "Execute", "ExecuteAsync", "Run", "RunAsync",
        "Invoke", "InvokeAsync", "Consume", "Process", "ProcessAsync"
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
        var words = SplitWords(name);
        if (words.Count > 0 && words[words.Count - 1] == "Async")
            words.RemoveAt(words.Count - 1);

        if (words.Count < 2)
            return false;

        // ClearAllData, DeleteAllSessions: a clearing verb, then All, then the whole set.
        // DeleteAllInactiveUsers names a subset and does not count.
        for (var i = 0; i + 2 < words.Count; i++)
        {
            if (!AllVerbs.Contains(words[i]) || words[i + 1] != "All")
                continue;

            var set = string.Concat(words.Skip(i + 2));
            if (WholeSetNouns.Contains(set) || NamesTable(set, tableNames))
                return true;
        }

        return ClearVerbs.Contains(words[0]) && NamesTable(string.Concat(words.Skip(1)), tableNames);
    }

    private static IEnumerable<string> GetIntentNames(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case LocalFunctionStatementSyntax localFunction:
                    yield return localFunction.Identifier.ValueText;
                    break;
                case MethodDeclarationSyntax method:
                {
                    var methodName = method.Identifier.ValueText;
                    if (!GenericEntryPointNames.Contains(methodName))
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
            yield return Pluralize(entityName);

        var current = invocation.GetInvocationReceiver()?.UnwrapConversions();
        while (current is IInvocationOperation chained && chained.GetInvocationReceiver() is { } receiver)
            current = receiver.UnwrapConversions();

        if (current is IPropertyReferenceOperation property && property.Type.IsDbSet())
        {
            var setName = property.Property.Name;
            if (!entityNames.Any(entityName => string.Equals(entityName, setName, StringComparison.OrdinalIgnoreCase)))
                yield return setName;
            yield return Pluralize(setName);
        }
    }

    private static bool NamesTable(string words, List<string> tableNames) =>
        tableNames.Any(tableName => string.Equals(words, tableName, StringComparison.OrdinalIgnoreCase));

    private static string Pluralize(string name)
    {
        if (name.Length == 0)
            return name;

        if (name.Length > 1 && name.EndsWith("y", StringComparison.Ordinal) && !IsVowel(name[name.Length - 2]))
            return name.Substring(0, name.Length - 1) + "ies";

        if (name.EndsWith("s", StringComparison.Ordinal) || name.EndsWith("x", StringComparison.Ordinal) ||
            name.EndsWith("z", StringComparison.Ordinal) || name.EndsWith("ch", StringComparison.Ordinal) ||
            name.EndsWith("sh", StringComparison.Ordinal))
            return name + "es";

        return name + "s";
    }

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
