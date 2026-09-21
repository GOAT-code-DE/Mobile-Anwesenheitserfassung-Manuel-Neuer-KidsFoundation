using Microsoft.EntityFrameworkCore;

namespace NeuerKids.Services;

public class RetentionWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            if (config.GetValue<bool>("Privacy:Approved") && config.GetValue<bool>("Privacy:AnonymizationApproved") && !config.GetValue<bool>("Demo:Enabled"))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<Domain.AppDbContext>();
                    var clock = scope.ServiceProvider.GetRequiredService<BerlinClock>();
                    var children = scope.ServiceProvider.GetRequiredService<ChildrenService>();
                    var months = config.GetValue<int>("Privacy:InactiveMonths");
                    var days = config.GetValue<int>("Privacy:AuditRetentionDays");
                    if (months <= 0 || days <= 0) continue;
                    var cutoff = clock.Today.AddMonths(-months);
                    var ids = await db.Children.Where(c => !c.Attendances.Any(a => a.Day >= cutoff) && c.CreatedOn < cutoff).Select(c => c.Id).ToListAsync(token);
                    foreach (var id in ids)
                    {
                        token.ThrowIfCancellationRequested();
                        // Reload each child; concurrency tokens and a serializable purge prevent racing edits.
                        var child = await db.Children.FindAsync([id], token);
                        if (child is not null && !await db.Attendances.AnyAsync(a => a.ChildId == id && a.Day >= cutoff, token)) await children.Purge(child, "retention", true);
                    }
                    var auditCutoff = clock.UtcNow.AddDays(-days);
                    await db.AuditEntries.Where(a => a.AtUtc < auditCutoff).ExecuteDeleteAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception ex) { logger.LogError("Automatische Löschung fehlgeschlagen ({Type}). Administrator muss den Lauf prüfen.", ex.GetType().Name); }
            }
        } while (await timer.WaitForNextTickAsync(token));
    }
}
