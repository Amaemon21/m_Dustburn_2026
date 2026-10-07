#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;

public static class GeneratedAssetFile
{
    private const int REPLACE_ATTEMPTS = 10;
    private const int RETRY_DELAY_MS = 100;
    private const int SHARING_VIOLATION = 32;
    private const int LOCK_VIOLATION = 33;
    private const int USER_MAPPED_FILE = 1224;
    private const int UNABLE_TO_REMOVE_REPLACED = 1175;
    private const int UNABLE_TO_MOVE_REPLACEMENT = 1177;

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        string destination = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(destination);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(directory);

        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(destination))
                        File.Replace(temporary, destination, null);
                    else
                        File.Move(temporary, destination);

                    return;
                }
                catch (IOException exception) when (IsFileBusy(exception) && attempt < REPLACE_ATTEMPTS - 1)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (attempt + 1));
                }
            }
        }
        catch (IOException exception) when (IsFileBusy(exception))
        {
            throw new IOException($"Cannot replace generated asset '{destination}' because another application is holding it open. Close its preview and retry generation. The asset was not opened for truncation.", exception);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static bool IsFileBusy(IOException exception)
    {
        int error = exception.HResult & 0xffff;
        return error is SHARING_VIOLATION or LOCK_VIOLATION or USER_MAPPED_FILE or UNABLE_TO_REMOVE_REPLACED or UNABLE_TO_MOVE_REPLACEMENT;
    }
}
#endif
