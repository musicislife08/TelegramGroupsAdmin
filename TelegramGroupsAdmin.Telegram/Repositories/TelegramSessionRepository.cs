using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public sealed class TelegramSessionRepository(
    IDbContextFactory<AppDbContext> contextFactory,
    IDataProtectionProvider dataProtectionProvider) : ITelegramSessionRepository
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(DataProtectionPurposes.TelegramSession);

    public async Task<TelegramSession?> GetActiveSessionAsync(string webUserId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dto = await context.TelegramSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(ts => ts.WebUserId == webUserId && ts.IsActive, ct);

        if (dto is null) return null;

        return dto.ToModel() with
        {
            SessionData = DecryptSessionData(dto.SessionData),
            PhoneNumber = DecryptPhoneNumber(dto.PhoneNumber)
        };
    }

    public async Task<List<TelegramSession>> GetAllActiveSessionsAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dtos = await context.TelegramSessions
            .AsNoTracking()
            .Where(ts => ts.IsActive)
            .ToListAsync(ct);

        return dtos.Select(d => d.ToModel() with
        {
            SessionData = DecryptSessionData(d.SessionData),
            PhoneNumber = DecryptPhoneNumber(d.PhoneNumber)
        }).ToList();
    }

    public async Task<bool> AnyActiveSessionExistsAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.TelegramSessions.AnyAsync(ts => ts.IsActive, ct);
    }

    public async Task<long> CreateSessionAsync(TelegramSession session, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dto = session.ToDto();
        dto.SessionData = EncryptSessionData(dto.SessionData);
        dto.PhoneNumber = EncryptPhoneNumber(dto.PhoneNumber);

        context.TelegramSessions.Add(dto);
        await context.SaveChangesAsync(ct);

        return dto.Id;
    }

    public async Task UpdateLastUsedAsync(long sessionId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.TelegramSessions
            .Where(ts => ts.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(ts => ts.LastUsedAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task UpdateSessionDataAsync(long sessionId, byte[] sessionData, CancellationToken ct)
    {
        var encrypted = EncryptSessionData(sessionData);
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.TelegramSessions
            .Where(ts => ts.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(ts => ts.SessionData, encrypted), ct);
    }

    public async Task DeactivateSessionAsync(long sessionId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.TelegramSessions
            .Where(ts => ts.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ts => ts.IsActive, false)
                .SetProperty(ts => ts.DisconnectedAt, DateTimeOffset.UtcNow)
                .SetProperty(ts => ts.SessionData, (byte[])[]), ct);
    }

    private byte[] EncryptSessionData(byte[] data)
    {
        if (data.Length == 0) return data;
        return _protector.Protect(data);
    }

    private byte[] DecryptSessionData(byte[] data)
    {
        if (data.Length == 0) return data;
        return _protector.Unprotect(data);
    }

    private string? EncryptPhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber)) return phoneNumber;
        return _protector.Protect(phoneNumber);
    }

    private string? DecryptPhoneNumber(string? encryptedPhoneNumber)
    {
        if (string.IsNullOrEmpty(encryptedPhoneNumber)) return encryptedPhoneNumber;
        return _protector.Unprotect(encryptedPhoneNumber);
    }
}
