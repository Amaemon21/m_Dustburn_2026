using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal static partial class Harness
{
    private static async Task VerifyGameSlotImages()
    {
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        string root = Path.GetFullPath(Path.Combine(temporaryRoot, "DustbornPreviewChecks_" + Guid.NewGuid().ToString("N")));
        Check(root.StartsWith(temporaryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "preview test files remain inside their temporary workspace");
        var store = new GameSlotImageStore(new SlotStorage(root));
        Expect<ArgumentException>(() => store.PathFor("../outside"), "preview paths reject parent traversal");
        Expect<ArgumentException>(() => store.PathFor("..\\outside"), "preview paths reject Windows parent traversal");
        Expect<ArgumentException>(() => store.PathFor(""), "preview paths reject empty save IDs");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "playthrough"));
            string destination = store.PathFor("playthrough");
            Check(destination == Path.Combine(root, "playthrough", "screenshot.png"),
                "a screenshot belongs to its playthrough rather than the shared map");
            await store.WriteAsync("playthrough", new byte[] { 1, 2, 3 }, CancellationToken.None);
            Check(File.ReadAllBytes(destination).SequenceEqual(new byte[] { 1, 2, 3 }), "a successful preview write persists its bytes");
            await store.WriteAsync("playthrough", new byte[] { 4, 5 }, CancellationToken.None);
            Check(File.ReadAllBytes(destination).SequenceEqual(new byte[] { 4, 5 }) && !File.Exists(destination + ".tmp"),
                "the newest preview replaces the old one without leaving a temporary image");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                await store.WriteAsync("playthrough", new byte[] { 9 }, cancelled.Token);
                Check(false, "cancelled image writes must not finish");
            }
            catch (OperationCanceledException) { }
            Check(File.ReadAllBytes(destination).SequenceEqual(new byte[] { 4, 5 }), "a cancelled preview keeps the previous image intact");
            await store.WriteAsync("deleted_save", new byte[] { 7 }, CancellationToken.None);
            Check(!Directory.Exists(Path.Combine(root, "deleted_save")), "late screenshot writes cannot recreate deleted playthroughs");
        }
        finally
        {
            if (!root.StartsWith(temporaryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid temporary cleanup path");
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
