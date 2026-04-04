# Username History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Track username/name changes in a normalized history table with global search integration and audit trail.

**Architecture:** New `username_history` table with FK cascade to `telegram_users`. Capture point is the existing `ProfileDiffDetected` block in `MessageProcessingService`. Search extends the existing `ILike` queries with an `OR EXISTS` subquery. UI adds a collapsed expansion panel to `UserDetailDialog`.

**Tech Stack:** EF Core 10, PostgreSQL 18, MudBlazor 9, NUnit, bUnit, NSubstitute

**Spec:** `docs/superpowers/specs/2026-04-03-username-history-design.md`

---

### Task 1: Add `ProfileChange` to `UserActionType` enums

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/UserActionType.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Models/UserActionType.cs`
- Modify: `TelegramGroupsAdmin.Core/Models/Actor.cs:100-117`

- [ ] **Step 1: Add enum value to Data layer**

In `TelegramGroupsAdmin.Data/Models/UserActionType.cs`, add after `RestorePermissions = 9`:

```csharp
    /// <summary>Profile change detected (username, first name, or last name changed)</summary>
    ProfileChange = 10
```

- [ ] **Step 2: Add enum value to Telegram layer**

In `TelegramGroupsAdmin.Telegram/Models/UserActionType.cs`, add after `RestorePermissions = 9`:

```csharp
    /// <summary>Profile change detected (username, first name, or last name changed)</summary>
    ProfileChange = 10
```

- [ ] **Step 3: Add display name mapping for Actor.ForSystem**

In `TelegramGroupsAdmin.Core/Models/Actor.cs`, in the `ForSystem` switch expression (around line 100-117), add:

```csharp
            "profile_diff_detection" => "Profile Change Detection",
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Data/Models/UserActionType.cs TelegramGroupsAdmin.Telegram/Models/UserActionType.cs TelegramGroupsAdmin.Core/Models/Actor.cs
git commit -m "feat: add ProfileChange user action type for name history tracking"
```

---

### Task 2: Create `UsernameHistoryDto` entity and EF Core configuration

**Files:**
- Create: `TelegramGroupsAdmin.Data/Models/UsernameHistoryDto.cs`
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs`

- [ ] **Step 1: Create the entity class**

Create `TelegramGroupsAdmin.Data/Models/UsernameHistoryDto.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Tracks historical username/name values for a Telegram user.
/// Each row captures the previous values at the moment a profile change is detected.
/// </summary>
[Table("username_history")]
public class UsernameHistoryDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("username")]
    [MaxLength(32)]
    public string? Username { get; set; }

    [Column("first_name")]
    [MaxLength(64)]
    public string? FirstName { get; set; }

    [Column("last_name")]
    [MaxLength(64)]
    public string? LastName { get; set; }

    [Column("recorded_at")]
    public DateTimeOffset RecordedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(UserId))]
    public virtual TelegramUserDto? User { get; set; }
}
```

- [ ] **Step 2: Add DbSet to AppDbContext**

In `TelegramGroupsAdmin.Data/AppDbContext.cs`, after the `TelegramUsers` DbSet (around line 37):

```csharp
    public DbSet<UsernameHistoryDto> UsernameHistory => Set<UsernameHistoryDto>();
```

- [ ] **Step 3: Add fluent configuration in OnModelCreating**

In `AppDbContext.OnModelCreating`, add a new section for username history configuration. Place it near the other `TelegramUsers`-related config:

```csharp
        // UsernameHistory → TelegramUsers (cascade delete)
        modelBuilder.Entity<UsernameHistoryDto>()
            .HasOne(h => h.User)
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // UsernameHistory indexes
        modelBuilder.Entity<UsernameHistoryDto>()
            .HasIndex(h => h.UserId)
            .HasDatabaseName("IX_username_history_user_id");

        modelBuilder.Entity<UsernameHistoryDto>()
            .HasIndex(h => h.Username)
            .HasDatabaseName("IX_username_history_username_lower");

        modelBuilder.Entity<UsernameHistoryDto>()
            .HasIndex(h => new { h.FirstName, h.LastName })
            .HasDatabaseName("IX_username_history_name_lower");
```

- [ ] **Step 4: Generate EF Core migration**

Run: `dotnet ef migrations add AddUsernameHistoryTable -p TelegramGroupsAdmin.Data -s TelegramGroupsAdmin`

Review the generated migration to ensure it creates the table with correct columns, FK, and indexes. Verify it does NOT drop/recreate any existing tables.

- [ ] **Step 5: Validate migration applies cleanly**

Run: `dotnet run --project TelegramGroupsAdmin --migrate-only`
Expected: Migration applies without errors.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Data/Models/UsernameHistoryDto.cs TelegramGroupsAdmin.Data/AppDbContext.cs TelegramGroupsAdmin.Data/Migrations/
git commit -m "feat: add username_history table with cascade delete and search indexes"
```

---

### Task 3: Create domain model, repository, and DI registration

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Models/UsernameHistoryRecord.cs`
- Create: `TelegramGroupsAdmin.Telegram/Repositories/Mappings/UsernameHistoryMappings.cs`
- Create: `TelegramGroupsAdmin.Telegram/Repositories/IUsernameHistoryRepository.cs`
- Create: `TelegramGroupsAdmin.Telegram/Repositories/UsernameHistoryRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs:60`

- [ ] **Step 1: Create domain model**

Create `TelegramGroupsAdmin.Telegram/Models/UsernameHistoryRecord.cs`:

```csharp
namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for a username history entry.
/// Each record captures the previous profile values at the moment a change was detected.
/// </summary>
public record UsernameHistoryRecord(
    long Id,
    long UserId,
    string? Username,
    string? FirstName,
    string? LastName,
    DateTimeOffset RecordedAt);
```

- [ ] **Step 2: Create mapping extensions**

Create `TelegramGroupsAdmin.Telegram/Repositories/Mappings/UsernameHistoryMappings.cs`:

```csharp
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

public static class UsernameHistoryMappings
{
    extension(DataModels.UsernameHistoryDto data)
    {
        public UiModels.UsernameHistoryRecord ToModel() => new(
            Id: data.Id,
            UserId: data.UserId,
            Username: data.Username,
            FirstName: data.FirstName,
            LastName: data.LastName,
            RecordedAt: data.RecordedAt);
    }
}
```

- [ ] **Step 3: Create repository interface**

Create `TelegramGroupsAdmin.Telegram/Repositories/IUsernameHistoryRepository.cs`:

```csharp
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public interface IUsernameHistoryRepository
{
    /// <summary>
    /// Record the previous profile values when a change is detected.
    /// </summary>
    Task InsertAsync(long userId, string? username, string? firstName, string? lastName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all history entries for a user, most recent first.
    /// </summary>
    Task<List<UsernameHistoryRecord>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create repository implementation**

Create `TelegramGroupsAdmin.Telegram/Repositories/UsernameHistoryRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class UsernameHistoryRepository(IDbContextFactory<AppDbContext> contextFactory) : IUsernameHistoryRepository
{
    public async Task InsertAsync(long userId, string? username, string? firstName, string? lastName, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        context.UsernameHistory.Add(new UsernameHistoryDto
        {
            UserId = userId,
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            RecordedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<UsernameHistoryRecord>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UsernameHistory
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.RecordedAt)
            .Select(h => h.ToModel())
            .ToListAsync(cancellationToken);
    }
}
```

- [ ] **Step 5: Register in DI**

In `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`, after the `ITelegramUserRepository` registration (line 60):

```csharp
            services.AddScoped<IUsernameHistoryRepository, UsernameHistoryRepository>();
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Models/UsernameHistoryRecord.cs TelegramGroupsAdmin.Telegram/Repositories/Mappings/UsernameHistoryMappings.cs TelegramGroupsAdmin.Telegram/Repositories/IUsernameHistoryRepository.cs TelegramGroupsAdmin.Telegram/Repositories/UsernameHistoryRepository.cs TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: add UsernameHistoryRepository with domain model and DI registration"
```

---

### Task 4: Wire capture logic into `MessageProcessingService`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs:672-689`

- [ ] **Step 1: Add history and action insertion to ProfileDiffDetected block**

In `MessageProcessingService.cs`, the `ProfileDiffDetected` block currently looks like this (after the hotfix):

```csharp
            if (existingUser is not null
                && contentCheckSkipReason == ContentCheckSkipReason.NotSkipped
                && ProfileDiffDetected(existingUser, message.From))
            {
                LogProfileChangeDetected(logger, message.From.ToLogDebug(), existingUser.ToLogInfo(), message.From.ToLogInfo());

                try
                {
                    var jobScheduler = messageScope.ServiceProvider.GetRequiredService<Handlers.BackgroundJobScheduler>();
                    await jobScheduler.ScheduleProfileScanAsync(message.From.Id, message.Chat.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to schedule profile scan for user {UserId}", message.From.Id);
                }
            }

            await telegramUserRepo.UpsertAsync(telegramUser, cancellationToken);
```

Replace with:

```csharp
            if (existingUser is not null
                && contentCheckSkipReason == ContentCheckSkipReason.NotSkipped
                && ProfileDiffDetected(existingUser, message.From))
            {
                LogProfileChangeDetected(logger, message.From.ToLogDebug(), existingUser.ToLogInfo(), message.From.ToLogInfo());

                // Record previous profile values in username_history
                var historyRepo = messageScope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
                await historyRepo.InsertAsync(
                    existingUser.TelegramUserId,
                    existingUser.Username,
                    existingUser.FirstName,
                    existingUser.LastName,
                    cancellationToken);

                // Record profile change in user_actions audit trail
                var userActionsRepo = messageScope.ServiceProvider.GetRequiredService<IUserActionsRepository>();
                var changeReason = BuildProfileChangeReason(existingUser, message.From);
                await userActionsRepo.InsertAsync(new UserActionRecord(
                    Id: 0,
                    UserId: existingUser.TelegramUserId,
                    ActionType: UserActionType.ProfileChange,
                    MessageId: message.MessageId,
                    ChatId: message.Chat.Id,
                    IssuedBy: Actor.ForSystem("profile_diff_detection"),
                    IssuedAt: DateTimeOffset.UtcNow,
                    ExpiresAt: null,
                    Reason: changeReason), cancellationToken);

                try
                {
                    var jobScheduler = messageScope.ServiceProvider.GetRequiredService<Handlers.BackgroundJobScheduler>();
                    await jobScheduler.ScheduleProfileScanAsync(message.From.Id, message.Chat.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to schedule profile scan for user {UserId}", message.From.Id);
                }
            }

            await telegramUserRepo.UpsertAsync(telegramUser, cancellationToken);
```

- [ ] **Step 2: Add the BuildProfileChangeReason helper method**

Add this private static method to `MessageProcessingService` (near the `ProfileDiffDetected` method, around line 807):

```csharp
    private static string BuildProfileChangeReason(TelegramUser old, global::Telegram.Bot.Types.User current)
    {
        var changes = new List<string>();

        if (!string.Equals(old.Username, current.Username, StringComparison.Ordinal))
            changes.Add($"Username: @{old.Username ?? "(none)"} → @{current.Username ?? "(none)"}");

        if (!string.Equals(old.FirstName, current.FirstName, StringComparison.Ordinal))
            changes.Add($"First name: {old.FirstName ?? "(none)"} → {current.FirstName ?? "(none)"}");

        if (!string.Equals(old.LastName, current.LastName, StringComparison.Ordinal))
            changes.Add($"Last name: {old.LastName ?? "(none)"} → {current.LastName ?? "(none)"}");

        return string.Join(", ", changes);
    }
```

- [ ] **Step 3: Add required usings**

At the top of `MessageProcessingService.cs`, ensure these usings are present (add any that are missing):

```csharp
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs
git commit -m "feat: capture username history and audit action on profile change"
```

---

### Task 5: Extend search queries to include username history

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs` (lines 487-494, 566-573, 674-681, ~1175)
- Modify: `TelegramGroupsAdmin/Components/Pages/Users.razor:23`

- [ ] **Step 1: Create a reusable search predicate helper**

In `TelegramUserRepository.cs`, add a private static method to avoid duplicating the history subquery across 4 search locations. Place it near the bottom of the class:

```csharp
    /// <summary>
    /// Builds a predicate that matches users by current OR past names from username_history.
    /// </summary>
    private static IQueryable<TelegramUserDto> ApplySearchFilter(
        IQueryable<TelegramUserDto> query, AppDbContext context, string search)
    {
        return query.Where(u =>
            (u.Username != null && EF.Functions.ILike(u.Username, $"%{search}%")) ||
            (u.FirstName != null && EF.Functions.ILike(u.FirstName, $"%{search}%")) ||
            (u.LastName != null && EF.Functions.ILike(u.LastName, $"%{search}%")) ||
            EF.Functions.ILike(u.TelegramUserId.ToString(), $"%{search}%") ||
            context.UsernameHistory.Any(h =>
                h.UserId == u.TelegramUserId &&
                ((h.Username != null && EF.Functions.ILike(h.Username, $"%{search}%")) ||
                 (h.FirstName != null && EF.Functions.ILike(h.FirstName, $"%{search}%")) ||
                 (h.LastName != null && EF.Functions.ILike(h.LastName, $"%{search}%")))));
    }
```

- [ ] **Step 2: Replace search block in GetPagedUsersAsync**

In `GetPagedUsersAsync` (around line 487-494), replace:

```csharp
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.Trim().ToLower();
            query = query.Where(u =>
                (u.Username != null && EF.Functions.ILike(u.Username, $"%{search}%")) ||
                (u.FirstName != null && EF.Functions.ILike(u.FirstName, $"%{search}%")) ||
                (u.LastName != null && EF.Functions.ILike(u.LastName, $"%{search}%")) ||
                EF.Functions.ILike(u.TelegramUserId.ToString(), $"%{search}%"));
```

With:

```csharp
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.Trim().ToLower();
            query = ApplySearchFilter(query, context, search);
```

- [ ] **Step 3: Replace search block in GetPagedBannedUsersWithDetailsAsync**

In `GetPagedBannedUsersWithDetailsAsync` (around line 566-573), apply the same replacement — replace the inline `query.Where(...)` with `ApplySearchFilter(query, context, search)`.

- [ ] **Step 4: Replace search block in GetUserTabCountsAsync**

In `GetUserTabCountsAsync` (around line 674-681), apply the same replacement — replace the inline `baseQuery.Where(...)` with `ApplySearchFilter(baseQuery, context, search)`.

- [ ] **Step 5: Update SearchByNameAsync**

In `SearchByNameAsync` (around line 1169), extend the existing fuzzy search query to also match against `username_history`. Add an `OR` clause using `context.UsernameHistory.Any(...)` following the same pattern as `ApplySearchFilter`, but adapted to the existing query structure of that method.

- [ ] **Step 6: Update search placeholder text**

In `TelegramGroupsAdmin/Components/Pages/Users.razor` line 23, change:

```razor
                      Placeholder="Search by name, username, or ID..."
```

To:

```razor
                      Placeholder="Search by name, username, ID, or past names..."
```

- [ ] **Step 7: Build to verify**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 8: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs TelegramGroupsAdmin/Components/Pages/Users.razor
git commit -m "feat: extend user search to match past usernames from history"
```

---

### Task 6: Add name history panel to UserDetailDialog

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/UserDetailDialog.razor`

- [ ] **Step 1: Inject the repository**

Add to the inject block at the top of `UserDetailDialog.razor`:

```razor
@inject IUsernameHistoryRepository UsernameHistoryRepo
```

And add the using if not already present:

```razor
@using TelegramGroupsAdmin.Telegram.Repositories
```

- [ ] **Step 2: Add state field and load history**

In the `@code` block, add a field:

```csharp
    private List<UsernameHistoryRecord> _nameHistory = [];
```

In the existing `OnParametersSetAsync` or `OnInitializedAsync` method (wherever user data is loaded), add after the user detail is loaded:

```csharp
    _nameHistory = await UsernameHistoryRepo.GetByUserIdAsync(UserId, cancellationToken);
```

- [ ] **Step 3: Add the expansion panel to the dialog body**

Add the name history panel in the dialog body, after the existing user info section. Only render when history exists:

```razor
@if (_nameHistory.Count > 0)
{
    <MudExpansionPanels Class="mt-4" Elevation="0">
        <MudExpansionPanel Text="@($"Name History ({_nameHistory.Count})")"
                           Icon="@Icons.Material.Filled.History"
                           IsInitiallyExpanded="false">
            <MudSimpleTable Dense="true" Hover="true" Striped="true">
                <thead>
                    <tr>
                        <th>Username</th>
                        <th>Name</th>
                        <th>Date</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var entry in _nameHistory)
                    {
                        <tr>
                            <td>@(entry.Username != null ? $"@{entry.Username}" : "(no username)")</td>
                            <td>@FormatName(entry.FirstName, entry.LastName)</td>
                            <td>@entry.RecordedAt.LocalDateTime.ToString("MMM d, yyyy")</td>
                        </tr>
                    }
                </tbody>
            </MudSimpleTable>
        </MudExpansionPanel>
    </MudExpansionPanels>
}
```

- [ ] **Step 4: Add the FormatName helper**

In the `@code` block:

```csharp
    private static string FormatName(string? firstName, string? lastName)
    {
        var parts = new[] { firstName, lastName }.Where(p => !string.IsNullOrEmpty(p));
        return parts.Any() ? string.Join(" ", parts) : "(no name)";
    }
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/UserDetailDialog.razor
git commit -m "feat: add collapsed name history panel to user detail dialog"
```

---

### Task 7: Integration tests for UsernameHistoryRepository

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/Repositories/UsernameHistoryRepositoryTests.cs`

- [ ] **Step 1: Create the test class**

Create `TelegramGroupsAdmin.IntegrationTests/Repositories/UsernameHistoryRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

[TestFixture]
[Category("Integration")]
public class UsernameHistoryRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    private const long TestUserId = 111222333;
    private const long OtherUserId = 444555666;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder => builder.AddConsole());
        services.AddScoped<IUsernameHistoryRepository, UsernameHistoryRepository>();
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_testHelper != null)
            await _testHelper.DisposeAsync();
    }

    private async Task SeedUserAsync(long userId, string? username = "testuser", string? firstName = "Test", string? lastName = "User")
    {
        using var scope = _serviceProvider!.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var now = DateTimeOffset.UtcNow;
        await userRepo.UpsertAsync(new TelegramUser(
            TelegramUserId: userId,
            Username: username,
            FirstName: firstName,
            LastName: lastName,
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: false,
            IsTrusted: false,
            IsBanned: false,
            KickCount: 0,
            BotDmEnabled: false,
            FirstSeenAt: now,
            LastSeenAt: now,
            CreatedAt: now,
            UpdatedAt: now));
    }

    [Test]
    public async Task InsertAsync_And_GetByUserIdAsync_RoundTrips()
    {
        await SeedUserAsync(TestUserId);

        using var scope = _serviceProvider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        await repo.InsertAsync(TestUserId, "old_username", "OldFirst", "OldLast");

        var history = await repo.GetByUserIdAsync(TestUserId);

        Assert.That(history, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(history[0].Username, Is.EqualTo("old_username"));
            Assert.That(history[0].FirstName, Is.EqualTo("OldFirst"));
            Assert.That(history[0].LastName, Is.EqualTo("OldLast"));
            Assert.That(history[0].UserId, Is.EqualTo(TestUserId));
        });
    }

    [Test]
    public async Task GetByUserIdAsync_ReturnsDescendingByRecordedAt()
    {
        await SeedUserAsync(TestUserId);

        using var scope = _serviceProvider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        await repo.InsertAsync(TestUserId, "first_name", "A", "A");
        await Task.Delay(50); // Ensure different timestamps
        await repo.InsertAsync(TestUserId, "second_name", "B", "B");

        var history = await repo.GetByUserIdAsync(TestUserId);

        Assert.That(history, Has.Count.EqualTo(2));
        Assert.That(history[0].Username, Is.EqualTo("second_name")); // Most recent first
        Assert.That(history[1].Username, Is.EqualTo("first_name"));
    }

    [Test]
    public async Task CascadeDelete_RemovesHistoryWhenUserDeleted()
    {
        await SeedUserAsync(TestUserId);

        using var scope = _serviceProvider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
        await repo.InsertAsync(TestUserId, "old_name", "Old", "Name");

        // Delete the parent user
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.TelegramUsers.Where(u => u.TelegramUserId == TestUserId).ExecuteDeleteAsync();

        // History should be gone
        var history = await repo.GetByUserIdAsync(TestUserId);
        Assert.That(history, Is.Empty);
    }

    [Test]
    public async Task GetByUserIdAsync_DoesNotReturnOtherUsersHistory()
    {
        await SeedUserAsync(TestUserId);
        await SeedUserAsync(OtherUserId, "otheruser");

        using var scope = _serviceProvider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        await repo.InsertAsync(TestUserId, "my_old_name", "My", "Name");
        await repo.InsertAsync(OtherUserId, "their_old_name", "Their", "Name");

        var history = await repo.GetByUserIdAsync(TestUserId);

        Assert.That(history, Has.Count.EqualTo(1));
        Assert.That(history[0].Username, Is.EqualTo("my_old_name"));
    }

    [Test]
    public async Task InsertAsync_HandlesNullFields()
    {
        await SeedUserAsync(TestUserId);

        using var scope = _serviceProvider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        await repo.InsertAsync(TestUserId, null, null, null);

        var history = await repo.GetByUserIdAsync(TestUserId);

        Assert.That(history, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(history[0].Username, Is.Null);
            Assert.That(history[0].FirstName, Is.Null);
            Assert.That(history[0].LastName, Is.Null);
        });
    }
}
```

- [ ] **Step 2: Run integration tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~UsernameHistoryRepositoryTests" -v normal 2>&1 | tail -20`
Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Repositories/UsernameHistoryRepositoryTests.cs
git commit -m "test: add integration tests for UsernameHistoryRepository"
```

---

### Task 8: Integration tests for search with username history

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs`

- [ ] **Step 1: Add search-by-past-username tests**

Add the following tests to the existing `TelegramUserRepositoryTests.cs` class. These tests verify that the search queries in `GetPagedUsersAsync` and `GetPagedBannedUsersWithDetailsAsync` match users by past names from `username_history`.

```csharp
    [Test]
    public async Task GetPagedUsersAsync_SearchMatchesPastUsername()
    {
        // Seed a user with current username "new_name"
        await SeedUserAsync(TestUserId, username: "new_name", firstName: "Current", lastName: "Name");

        // Insert history entry with old username
        using var scope = _serviceProvider!.CreateScope();
        var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
        await historyRepo.InsertAsync(TestUserId, "old_name", "Previous", "Name");

        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var (items, totalCount) = await userRepo.GetPagedUsersAsync(
            UserListFilter.All, skip: 0, take: 10,
            searchText: "old_name", chatIds: null,
            sortLabel: null, sortDescending: false);

        Assert.That(totalCount, Is.EqualTo(1));
        Assert.That(items[0].TelegramUserId, Is.EqualTo(TestUserId));
    }

    [Test]
    public async Task GetPagedUsersAsync_SearchMatchesPastFirstName()
    {
        await SeedUserAsync(TestUserId, username: "user1", firstName: "CurrentFirst", lastName: "Name");

        using var scope = _serviceProvider!.CreateScope();
        var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
        await historyRepo.InsertAsync(TestUserId, "user1", "OldFirst", "Name");

        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var (items, totalCount) = await userRepo.GetPagedUsersAsync(
            UserListFilter.All, skip: 0, take: 10,
            searchText: "OldFirst", chatIds: null,
            sortLabel: null, sortDescending: false);

        Assert.That(totalCount, Is.EqualTo(1));
        Assert.That(items[0].TelegramUserId, Is.EqualTo(TestUserId));
    }

    [Test]
    public async Task GetUserTabCountsAsync_IncludesUsersMatchedByPastNames()
    {
        await SeedUserAsync(TestUserId, username: "new_name", firstName: "Current", lastName: "Name");

        using var scope = _serviceProvider!.CreateScope();
        var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
        await historyRepo.InsertAsync(TestUserId, "old_name", "Previous", "Name");

        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var counts = await userRepo.GetUserTabCountsAsync(chatIds: null, searchText: "old_name");

        Assert.That(counts.ActiveCount, Is.EqualTo(1));
    }
```

Note: You will need to register `IUsernameHistoryRepository` in the test's `SetUp` method's `ServiceCollection` alongside the existing repository registrations:

```csharp
        services.AddScoped<IUsernameHistoryRepository, UsernameHistoryRepository>();
```

- [ ] **Step 2: Run the search tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TelegramUserRepositoryTests" -v normal 2>&1 | tail -20`
Expected: All tests pass (existing + new).

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs
git commit -m "test: add integration tests for search by past usernames"
```

---

### Task 9: Component tests for name history panel

**Files:**
- Create: `TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogHistoryTests.cs`

- [ ] **Step 1: Create the component test class**

Create `TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogHistoryTests.cs`. Follow the same pattern as the existing `UserDetailDialogTests.cs` — extend `MudBlazorTestContext`, mock the same services, and add `IUsernameHistoryRepository` mock:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.ComponentTests.Components;

[TestFixture]
public class UserDetailDialogHistoryTests : MudBlazorTestContext
{
    private ITelegramUserManagementService _mockUserService = null!;
    private IBotModerationService _mockModerationService = null!;
    private IAdminNotesRepository _mockNotesRepo = null!;
    private IUserTagsRepository _mockTagsRepo = null!;
    private ITagDefinitionsRepository _mockTagDefinitionsRepo = null!;
    private IUserActionsRepository _mockActionsRepo = null!;
    private IUsernameHistoryRepository _mockHistoryRepo = null!;
    private IDialogService _dialogService = null!;

    private const long TestUserId = 123456789;

    public UserDetailDialogHistoryTests()
    {
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        this.AddTestWebUser();
    }

    [SetUp]
    public void SetUp()
    {
        _mockUserService = Substitute.For<ITelegramUserManagementService>();
        _mockModerationService = Substitute.For<IBotModerationService>();
        _mockNotesRepo = Substitute.For<IAdminNotesRepository>();
        _mockTagsRepo = Substitute.For<IUserTagsRepository>();
        _mockTagDefinitionsRepo = Substitute.For<ITagDefinitionsRepository>();
        _mockActionsRepo = Substitute.For<IUserActionsRepository>();
        _mockHistoryRepo = Substitute.For<IUsernameHistoryRepository>();

        Services.AddSingleton(_mockUserService);
        Services.AddSingleton(_mockModerationService);
        Services.AddSingleton(_mockNotesRepo);
        Services.AddSingleton(_mockTagsRepo);
        Services.AddSingleton(_mockTagDefinitionsRepo);
        Services.AddSingleton(_mockActionsRepo);
        Services.AddSingleton(_mockHistoryRepo);
    }

    private void SetupUserDetailReturn(TelegramUserDetail detail)
    {
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(detail);
    }

    [Test]
    public async Task NameHistoryPanel_NotRendered_WhenNoHistory()
    {
        // Arrange - user exists, no history
        _mockHistoryRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(new List<UsernameHistoryRecord>());

        // Setup minimal user detail (adapt to actual constructor)
        // ... render dialog and assert no expansion panel with "Name History" text

        // Assert
        // Find the dialog provider markup and verify "Name History" text is absent
    }

    [Test]
    public async Task NameHistoryPanel_RenderedCollapsed_WhenHistoryExists()
    {
        // Arrange
        _mockHistoryRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(new List<UsernameHistoryRecord>
            {
                new(1, TestUserId, "old_user", "Old", "Name", DateTimeOffset.UtcNow.AddDays(-7)),
            });

        // ... render dialog and verify expansion panel exists with "Name History (1)" text
        // Verify panel is collapsed (content not visible)
    }

    [Test]
    public async Task NameHistoryPanel_ShowsCorrectData_WhenExpanded()
    {
        // Arrange
        var entries = new List<UsernameHistoryRecord>
        {
            new(2, TestUserId, "recent_user", "Recent", "Name", DateTimeOffset.UtcNow.AddDays(-1)),
            new(1, TestUserId, "oldest_user", "Oldest", "Name", DateTimeOffset.UtcNow.AddDays(-30)),
        };
        _mockHistoryRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(entries);

        // ... render dialog, expand the panel, verify:
        // - "@recent_user" appears before "@oldest_user"
        // - "Recent Name" and "Oldest Name" displayed
        // - Dates formatted correctly
    }

    [Test]
    public async Task NameHistoryPanel_HandlesNullUsername()
    {
        // Arrange
        _mockHistoryRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(new List<UsernameHistoryRecord>
            {
                new(1, TestUserId, null, "NoUsername", "User", DateTimeOffset.UtcNow),
            });

        // ... render dialog, expand panel, verify "(no username)" displayed
    }
}
```

Note: The test bodies above are intentionally sketched — the implementer must adapt them to match the actual `UserDetailDialog` rendering pattern from the existing `UserDetailDialogTests.cs` (dialog parameters, how to open the dialog, how to find markup). The assertions and mock setup are correct; the rendering boilerplate should be copied from the existing test file.

- [ ] **Step 2: Run component tests**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~UserDetailDialogHistoryTests" -v normal 2>&1 | tail -20`
Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogHistoryTests.cs
git commit -m "test: add component tests for name history panel in UserDetailDialog"
```

---

### Task 10: Unit tests for capture logic

**Files:**
- Create: `TelegramGroupsAdmin.UnitTests/Telegram/Services/MessageProcessingServiceProfileDiffTests.cs`

- [ ] **Step 1: Create the unit test class**

This tests the `BuildProfileChangeReason` method and verifies the correct services are called during profile diff handling. Since `MessageProcessingService` is a complex singleton with many dependencies, the most practical approach is to test the `BuildProfileChangeReason` helper directly (extract it as `internal static` if needed) and use integration-level verification for the full flow.

Create `TelegramGroupsAdmin.UnitTests/Telegram/Services/MessageProcessingServiceProfileDiffTests.cs`:

```csharp
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for profile diff reason building logic in MessageProcessingService.
/// The full capture flow (history insert + action insert) is verified by integration tests.
/// </summary>
[TestFixture]
public class MessageProcessingServiceProfileDiffTests
{
    [Test]
    public void BuildProfileChangeReason_UsernameChanged_IncludesUsernameInReason()
    {
        var old = CreateUser(username: "old_user");
        var current = CreateSdkUser(username: "new_user");

        var reason = InvokeBuildReason(old, current);

        Assert.That(reason, Does.Contain("Username: @old_user → @new_user"));
    }

    [Test]
    public void BuildProfileChangeReason_FirstNameChanged_IncludesFirstNameInReason()
    {
        var old = CreateUser(firstName: "OldFirst");
        var current = CreateSdkUser(firstName: "NewFirst");

        var reason = InvokeBuildReason(old, current);

        Assert.That(reason, Does.Contain("First name: OldFirst → NewFirst"));
    }

    [Test]
    public void BuildProfileChangeReason_LastNameChanged_IncludesLastNameInReason()
    {
        var old = CreateUser(lastName: "OldLast");
        var current = CreateSdkUser(lastName: "NewLast");

        var reason = InvokeBuildReason(old, current);

        Assert.That(reason, Does.Contain("Last name: OldLast → NewLast"));
    }

    [Test]
    public void BuildProfileChangeReason_MultipleFieldsChanged_IncludesAll()
    {
        var old = CreateUser(username: "old_user", firstName: "OldFirst", lastName: "OldLast");
        var current = CreateSdkUser(username: "new_user", firstName: "NewFirst", lastName: "NewLast");

        var reason = InvokeBuildReason(old, current);

        Assert.Multiple(() =>
        {
            Assert.That(reason, Does.Contain("Username:"));
            Assert.That(reason, Does.Contain("First name:"));
            Assert.That(reason, Does.Contain("Last name:"));
        });
    }

    [Test]
    public void BuildProfileChangeReason_NullToValue_ShowsNone()
    {
        var old = CreateUser(username: null);
        var current = CreateSdkUser(username: "new_user");

        var reason = InvokeBuildReason(old, current);

        Assert.That(reason, Does.Contain("@(none) → @new_user"));
    }

    [Test]
    public void BuildProfileChangeReason_ValueToNull_ShowsNone()
    {
        var old = CreateUser(username: "old_user");
        var current = CreateSdkUser(username: null);

        var reason = InvokeBuildReason(old, current);

        Assert.That(reason, Does.Contain("@old_user → @(none)"));
    }

    // Helper: create a TelegramUser with defaults
    private static TelegramGroupsAdmin.Telegram.Models.TelegramUser CreateUser(
        string? username = "testuser", string? firstName = "Test", string? lastName = "User")
    {
        var now = DateTimeOffset.UtcNow;
        return new TelegramGroupsAdmin.Telegram.Models.TelegramUser(
            TelegramUserId: 12345,
            Username: username,
            FirstName: firstName,
            LastName: lastName,
            UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
            IsBot: false, IsTrusted: false, IsBanned: false,
            KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: now, LastSeenAt: now, CreatedAt: now, UpdatedAt: now);
    }

    // Helper: create an SDK User with defaults
    private static Telegram.Bot.Types.User CreateSdkUser(
        string? username = "testuser", string? firstName = "Test", string? lastName = "User")
    {
        return new Telegram.Bot.Types.User
        {
            Id = 12345,
            IsBot = false,
            FirstName = firstName ?? "Test",
            LastName = lastName,
            Username = username,
        };
    }

    // Helper: invoke the private static method via reflection (or make it internal + InternalsVisibleTo)
    private static string InvokeBuildReason(
        TelegramGroupsAdmin.Telegram.Models.TelegramUser old,
        Telegram.Bot.Types.User current)
    {
        var method = typeof(MessageProcessingService)
            .GetMethod("BuildProfileChangeReason",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string)method!.Invoke(null, [old, current])!;
    }
}
```

Note: If reflection is not preferred, change `BuildProfileChangeReason` to `internal static` and add `[assembly: InternalsVisibleTo("TelegramGroupsAdmin.UnitTests")]` to the Telegram project. Check the existing pattern in the project.

- [ ] **Step 2: Run unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~MessageProcessingServiceProfileDiffTests" -v normal 2>&1 | tail -20`
Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.UnitTests/Telegram/Services/MessageProcessingServiceProfileDiffTests.cs
git commit -m "test: add unit tests for BuildProfileChangeReason logic"
```

---

### Task 11: Final verification

**Files:** None (verification only)

- [ ] **Step 1: Run full build**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 2: Run all unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests -v normal 2>&1 | tail -10`
Expected: All tests pass, no regressions.

- [ ] **Step 3: Run component tests**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests -v normal 2>&1 | tail -10`
Expected: All tests pass.

- [ ] **Step 4: Run integration tests** (background — takes ~20min)

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests -v normal > /tmp/integration-test-results.txt 2>&1`
Expected: All tests pass. Check results with `tail -20 /tmp/integration-test-results.txt`.

- [ ] **Step 5: Validate migration applies cleanly**

Run: `dotnet run --project TelegramGroupsAdmin --migrate-only`
Expected: Clean exit, no errors.

- [ ] **Step 6: Final commit (if any fixups needed)**

Only if previous steps required fixes. Otherwise, the feature is complete.
