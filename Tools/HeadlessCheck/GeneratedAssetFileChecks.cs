using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;

public static class GeneratedAssetFileChecks
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"DustbornAssetWrite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, "HeightMapPreview.png");
            byte[] original = Enumerable.Repeat((byte)17, 4096).ToArray();
            byte[] replacement = Enumerable.Repeat((byte)29, 8192).ToArray();
            string metadata = path + ".meta";
            File.WriteAllText(metadata, "guid: regression-test");

            GeneratedAssetFile.WriteAllBytes(path, original);
            Require(File.ReadAllBytes(path).SequenceEqual(original), "A new asset must be written completely.");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                CheckMappedFile(path, original, replacement);

            GeneratedAssetFile.WriteAllBytes(path, new byte[] { 42 });
            Require(File.ReadAllBytes(path).SequenceEqual(new byte[] { 42 }), "Shorter replacements must not retain old bytes.");
            Require(File.ReadAllText(metadata) == "guid: regression-test", "Replacing asset data must not change its GUID metadata.");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                CheckExclusiveReader(path, replacement);

            Require(Directory.GetFiles(directory, "*.tmp").Length == 0, "Temporary files must be cleaned after success and failure.");
            Console.WriteLine("PASS: asset creation, mapped-file replacement, shorter replacement, metadata preservation and locked-file failure safety.");
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory))
                File.Delete(file);

            Directory.Delete(directory);
        }
    }

    private static void CheckMappedFile(string path, byte[] original, byte[] replacement)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var mapping = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, true);
        using var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        bool reproduced = false;

        try
        {
            File.WriteAllBytes(path, replacement);
        }
        catch (IOException exception) when ((exception.HResult & 0xffff) == 1224)
        {
            reproduced = true;
        }

        Require(reproduced, "The regression must reproduce Windows error 1224 with the previous writer.");
        GeneratedAssetFile.WriteAllBytes(path, replacement);
        Require(File.ReadAllBytes(path).SequenceEqual(replacement), "A mapped asset must be replaced with the complete new file.");
        Require(view.ReadByte(0) == original[0], "Existing mapped readers must retain a valid view of the old file.");
        Console.WriteLine("PASS: reproduced Win32 1224; replacement succeeded while the old file remained mapped.");
    }

    private static void CheckExclusiveReader(string path, byte[] replacement)
    {
        byte[] previous = File.ReadAllBytes(path);
        bool rejected = false;

        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                GeneratedAssetFile.WriteAllBytes(path, replacement);
            }
            catch (IOException)
            {
                rejected = true;
            }
            catch (UnauthorizedAccessException)
            {
                rejected = true;
            }
        }

        Require(rejected, "A reader denying replacement must prevent the write.");
        Require(File.ReadAllBytes(path).SequenceEqual(previous), "A rejected write must leave existing bytes intact.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
