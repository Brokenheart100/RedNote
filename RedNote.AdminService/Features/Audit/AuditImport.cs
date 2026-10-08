using System.Text.Json;
using RedNote.AdminService.Infrastructure.Persistence;
using RedNote.Contracts.Admin;

namespace RedNote.AdminService.Features.Audit;

public static class AuditImport
{
    // Explicit maintenance command, not a runtime connection to another service's database.
    public static async Task<int> ImportAsync(string file, AdminServiceDbContext db, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(file);
        var records = await JsonSerializer.DeserializeAsync<AdminAuditRecorded[]>(stream, JsonSerializerOptions.Web, ct)
            ?? throw new InvalidDataException("Expected an audit array.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var record in records) await AdminAuditRecordedHandler.Handle(record, db, ct);
        await transaction.CommitAsync(ct);
        return records.Length;
    }
}
