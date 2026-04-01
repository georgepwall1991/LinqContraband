using System;
using System.IO;

namespace LinqContraband.Tests.Architecture;

internal static class RepositoryLayout
{
    public static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LinqContraband.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the current test base directory.");
    }
}
