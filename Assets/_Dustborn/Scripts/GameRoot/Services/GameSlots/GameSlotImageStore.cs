using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

public sealed class GameSlotImageStore
{
    public const string FILE_NAME = "screenshot.png";
    private readonly string _root;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public GameSlotImageStore(ISaveStorage storage)
    {
        _root = Path.GetFullPath(storage.Root);
    }

    public string PathFor(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId) || slotId == "." || slotId == ".." ||
            slotId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || slotId.Contains("/") || slotId.Contains("\\"))
            throw new ArgumentException("Invalid save slot id", nameof(slotId));
        string result = Path.GetFullPath(Path.Combine(_root, slotId, FILE_NAME));
        if (!result.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Image path must remain inside the save folder", nameof(slotId));
        return result;
    }

    public async UniTask WriteAsync(string slotId, byte[] png, CancellationToken token)
    {
        string destination = PathFor(slotId);
        await _writeLock.WaitAsync(token);
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                string directory = Path.GetDirectoryName(destination);
                if (!Directory.Exists(directory))
                    return;
                string temporary = destination + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, png);
                    token.ThrowIfCancellationRequested();
                    if (File.Exists(destination))
                        File.Replace(temporary, destination, null);
                    else
                        File.Move(temporary, destination);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
            }, token);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
