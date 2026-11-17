using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Zalihe.Infrastructure.Persistence;

/// <summary>
/// Changes saved inside a transaction (stock movements, orders, receipts) are announced only once it commits,
/// so open screens never reload data that is rolled back or not yet visible to other connections.
/// </summary>
public sealed class DataChangeTransactionInterceptor : DbTransactionInterceptor
{
    public static readonly DataChangeTransactionInterceptor Instance = new();

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        // Leftovers of an earlier transaction that was disposed without a commit.
        (eventData.Context as AppDbContext)?.DiscardChanges();
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
        DbTransaction result, CancellationToken cancellationToken = default)
    {
        (eventData.Context as AppDbContext)?.DiscardChanges();
        return ValueTask.FromResult(result);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        _ = (eventData.Context as AppDbContext)?.AnnounceChangesAsync();

    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext context) await context.AnnounceChangesAsync();
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        (eventData.Context as AppDbContext)?.DiscardChanges();

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        (eventData.Context as AppDbContext)?.DiscardChanges();
        return Task.CompletedTask;
    }
}
