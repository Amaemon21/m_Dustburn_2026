using System.Threading;
using Cysharp.Threading.Tasks;

public interface ISaveStorage
{
    string Root { get; }

    UniTask<string> ReadAsync(string relativePath, CancellationToken token);
    UniTask WriteAsync(string relativePath, string content, CancellationToken token);
    UniTask<bool> ExistsAsync(string relativePath, CancellationToken token);
    UniTask DeleteAsync(string relativePath, CancellationToken token);
    UniTask DeleteDirectoryAsync(string relativePath, CancellationToken token);
    UniTask<string[]> ListDirectoriesAsync(string relativePath, CancellationToken token);
}
