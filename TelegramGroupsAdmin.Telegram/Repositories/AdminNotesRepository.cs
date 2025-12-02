using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing admin notes on Telegram users
/// </summary>
public class AdminNotesRepository : IAdminNotesRepository
{
    private readonly AppDbContext _context;

    public AdminNotesRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<AdminNote>> GetNotesByUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var notes = await _context.AdminNotes
            .Where(n => n.TelegramUserId == telegramUserId)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                Note = n,
                WebUserEmail = n.ActorWebUserId != null
                    ? _context.Users.Where(u => u.Id == n.ActorWebUserId).Select(u => u.Email).FirstOrDefault()
                    : null,
                TelegramUser = n.ActorTelegramUserId != null
                    ? _context.TelegramUsers.Where(t => t.TelegramUserId == n.ActorTelegramUserId).FirstOrDefault()
                    : null
            })
            .ToListAsync(cancellationToken);

        return notes.Select(n => n.Note.ToModel(
            webUserEmail: n.WebUserEmail,
            telegramUsername: n.TelegramUser?.Username,
            telegramFirstName: n.TelegramUser?.FirstName,
            telegramLastName: n.TelegramUser?.LastName
        )).ToList();
    }

    public async Task<AdminNote?> GetNoteByIdAsync(long noteId, CancellationToken cancellationToken = default)
    {
        var note = await _context.AdminNotes
            .FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);

        return note?.ToModel();
    }

    public async Task<long> AddNoteAsync(AdminNote note, CancellationToken cancellationToken = default)
    {
        var dto = note.ToDto();
        dto.CreatedAt = DateTimeOffset.UtcNow;
        dto.UpdatedAt = null;

        _context.AdminNotes.Add(dto);
        await _context.SaveChangesAsync(cancellationToken);

        return dto.Id;
    }

    public async Task<bool> UpdateNoteAsync(AdminNote note, CancellationToken cancellationToken = default)
    {
        var existing = await _context.AdminNotes
            .FirstOrDefaultAsync(n => n.Id == note.Id, cancellationToken);

        if (existing == null)
            return false;

        existing.NoteText = note.NoteText;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.IsPinned = note.IsPinned;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteNoteAsync(long noteId, Actor deletedBy, CancellationToken cancellationToken = default)
    {
        var note = await _context.AdminNotes
            .FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);

        if (note == null)
            return false;

        _context.AdminNotes.Remove(note);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<AdminNote>> GetPinnedNotesByUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var notes = await _context.AdminNotes
            .Where(n => n.TelegramUserId == telegramUserId && n.IsPinned)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                Note = n,
                WebUserEmail = n.ActorWebUserId != null
                    ? _context.Users.Where(u => u.Id == n.ActorWebUserId).Select(u => u.Email).FirstOrDefault()
                    : null,
                TelegramUser = n.ActorTelegramUserId != null
                    ? _context.TelegramUsers.Where(t => t.TelegramUserId == n.ActorTelegramUserId).FirstOrDefault()
                    : null
            })
            .ToListAsync(cancellationToken);

        return notes.Select(n => n.Note.ToModel(
            webUserEmail: n.WebUserEmail,
            telegramUsername: n.TelegramUser?.Username,
            telegramFirstName: n.TelegramUser?.FirstName,
            telegramLastName: n.TelegramUser?.LastName
        )).ToList();
    }

    public async Task<bool> TogglePinAsync(long noteId, Actor toggledBy, CancellationToken cancellationToken = default)
    {
        var note = await _context.AdminNotes
            .FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);

        if (note == null)
            return false;

        note.IsPinned = !note.IsPinned;
        note.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
