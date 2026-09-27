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
    /// <c>db.ActivityLog.ExecuteDeleteAsync()</c>. The name must either pair a clearing verb with
    /// <c>All</c> (<c>ClearAllData</c>, <c>DeleteAllUsers</c>) or be a clearing verb followed by the
    /// plural of the entity or set (<c>ClearMentions</c> over <c>MentionEntity</c>). <c>ClearUserSession</c>
    /// over <c>Sessions</c> does not qualify.
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
        if (words.Count < 2)
            return false;

        // ClearAllData, DeleteAllSessions: a clearing verb, then All, then what is cleared.
        for (var i = 0; i + 2 < words.Count; i++)
        {
            if (AllVerbs.Contains(words[i]) && words[i + 1] == "All")
                return true;
        }

        if (!ClearVerbs.Contains(words[0]))
            return false;

        var rest = string.Concat(words.Skip(1));
        return tableNames.Any(tableName => MatchesPlural(rest, tableName));
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

    private static IEnumerable<string> GetTableNames(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.TypeArguments.FirstOrDefault() is { } entityType)
        {
            var entityName = entityType.Name;
            yield return entityName;
            if (entityName.Length > "Entity".Length && entityName.EndsWith("Entity", StringComparison.Ordinal))
                yield return entityName.Substring(0, entityName.Length - "Entity".Length);
        }

        var current = invocation.GetInvocationReceiver()?.UnwrapConversions();
        while (current is IInvocationOperation chained && chained.GetInvocationReceiver() is { } receiver)
            current = receiver.UnwrapConversions();

        if (current is IPropertyReferenceOperation property && property.Type.IsDbSet())
            yield return property.Property.Name;
    }

    private static bool MatchesPlural(string rest, string tableName)
    {
        if (string.Equals(rest, tableName, StringComparison.OrdinalIgnoreCase))
            return tableName.EndsWith("s", StringComparison.Ordinal);

        if (string.Equals(rest, tableName + "s", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rest, tableName + "es", StringComparison.OrdinalIgnoreCase))
            return true;

        return tableName.EndsWith("y", StringComparison.Ordinal) &&
               string.Equals(rest, tableName.Substring(0, tableName.Length - 1) + "ies", StringComparison.OrdinalIgnoreCase);
    }

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
