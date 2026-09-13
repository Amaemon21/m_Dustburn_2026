using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class FileSaveStorage : ISaveStorage
{
    private const string TEMP_EXTENSION = ".tmp";
    private const string BACKUP_EXTENSION = ".bak";

    private readonly string _root;

    public string Root => _root;

    public FileSaveStorage(string root)
    {
        _root = root;
    }

    public UniTask<string> ReadAsync(string relativePath, CancellationToken token)
    {
        string fullPath = Path.Combine(_root, relativePath);
        return UniTask.RunOnThreadPool(() => Read(fullPath), cancellationToken: token);
    }

    public UniTask WriteAsync(string relativePath, string content, CancellationToken token)
    {
        string fullPath = Path.Combine(_root, relativePath);
        return UniTask.RunOnThreadPool(() => Write(fullPath, content), cancellationToken: token);
    }

    public UniTask<bool> ExistsAsync(string relativePath, CancellationToken token)
    {
        string fullPath = Path.Combine(_root, relativePath);
        return UniTask.RunOnThreadPool(() => File.Exists(fullPath), cancellationToken: token);
    }

    public UniTask DeleteAsync(string relativePath, CancellationToken token)
    {
        string fullPath = Path.Combine(_root, relativePath);

        return UniTask.RunOnThreadPool(() =>
        {
            DeleteIfExists(fullPath);
            DeleteIfExists(fullPath + TEMP_EXTENSION);
            DeleteIfExists(fullPath + BACKUP_EXTENSION);
        }, cancellationToken: token);
    }

    public UniTask DeleteDirectoryAsync(string relativePath, CancellationToken token)
    {
        string fullPath = Path.Combine(_root, relativePath);

        return UniTask.RunOnThreadPool(() =>
        {
            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, true);
        }, cancellationToken: token);
    }

    public UniTask<string[]> ListDirectoriesAsync(string relativePath, CancellationToken token)
    {
        string fullPath = string.IsNullOrEmpty(relativePath) ? _root : Path.Combine(_root, relativePath);

        return UniTask.RunOnThreadPool(() =>
        {
            if (!Directory.Exists(fullPath))
                return Array.Empty<string>();

            string[] directories = Directory.GetDirectories(fullPath);

            for (int i = 0; i < directories.Length; i++)
                directories[i] = Path.GetFileName(directories[i]);

            return directories;
        }, cancellationToken: token);
    }

    private static string Read(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            try
            {
                return File.ReadAllText(fullPath, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Save read failed for {fullPath}: {exception.Message}");
            }
        }

        string backup = fullPath + BACKUP_EXTENSION;

        if (!File.Exists(backup))
            return null;

        Debug.LogWarning($"Falling back to backup save file {backup}");

        try
        {
            return File.ReadAllText(backup, Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Save backup read failed for {backup}: {exception.Message}");
            return null;
        }
    }

    private static void Write(string fullPath, string content)
    {
        string directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temp = fullPath + TEMP_EXTENSION;
        File.WriteAllText(temp, content, Encoding.UTF8);

        if (!File.Exists(fullPath))
        {
            File.Move(temp, fullPath);
            return;
        }

        string backup = fullPath + BACKUP_EXTENSION;

        try
        {
            File.Replace(temp, fullPath, backup, true);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Atomic replace unavailable for {fullPath}, falling back to copy: {exception.Message}");

            File.Copy(fullPath, backup, true);
            File.Copy(temp, fullPath, true);
            File.Delete(temp);
        }
    }

    private static void DeleteIfExists(string fullPath)
    {
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
