using System.Threading;
using System.Threading.Tasks;

namespace BPGit.Server.Services;

public sealed class RepositoryOperationLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task WaitAsync(CancellationToken cancellationToken = default)
        => _semaphore.WaitAsync(cancellationToken);

    public void Release() => _semaphore.Release();
}
