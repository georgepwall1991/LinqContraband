using System;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC011_EntityMissingPrimaryKey
{
    public class EntityMissingPrimaryKeySample
    {
        // LC011 is a Design-time analyzer.
        // It checks the definitions in AppDbContext.cs
        public static void Run()
        {
            Console.WriteLine("Testing LC011 (Design-time check, see AppDbContext.cs)...");
        }
    }
}

