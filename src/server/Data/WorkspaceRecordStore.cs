using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace Charac.Server.Data;

/// <summary>Records observed workspace endings without treating history as resumable sessions.</summary>
internal sealed class WorkspaceRecordStore(IServiceScopeFactory scopes, ILogger<WorkspaceRecordStore> logger)
{
    private readonly ConcurrentDictionary<Guid, Task> _pending = new();

    public void Closed(Guid id, string reason)
    {
        var task = MarkClosedAsync(id, reason);
        _pending[id] = task;
        _ = task.ContinueWith(completed => _pending.TryRemove(id, out var removed),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public Task FlushAsync() => Task.WhenAll(_pending.Values.ToArray());

    private async Task MarkClosedAsync(Guid id, string reason)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
            await database.WorkspaceRecords.Where(x => x.Id == id && x.ClosedAt == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.ClosedAt, DateTimeOffset.UtcNow)
                    .SetProperty(x => x.EndReason, reason));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not record closure of workspace {WorkspaceId}.", id);
        }
    }
}
