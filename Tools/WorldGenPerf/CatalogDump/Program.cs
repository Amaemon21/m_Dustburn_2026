using System;
using System.IO;

public static class CatalogDump
{
    public static void Main(string[] arguments)
    {
        string csv = WorldGenBenchCatalog.Csv();

        if (arguments.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arguments[0])));
            File.WriteAllText(arguments[0], csv);
            Console.WriteLine($"{WorldGenBenchCatalog.All.Count} specifications written to {arguments[0]}");
            return;
        }

        Console.Write(csv);
    }
}
