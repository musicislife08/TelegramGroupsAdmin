# Users All Tab + Action History Chat Context Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Guarantee every Telegram user is findable on the Users page via a predicate-free All tab, centralize join-gate activation so no admission path leaves a user inactive, and show which chat each action-history entry belongs to.

**Architecture:** The Users page is a MudBlazor `MudTabs` over server-paged `MudTable`s; each tab passes a `UserListFilter` enum value to `TelegramUserRepository.GetPagedUsersAsync`, which builds an EF Core query. We add an `All` filter that applies no status predicate, expose `IsActive` on the list item so All can flag unverified users, move `ActivateAsync` into `WelcomeAdmissionHandler.TryAdmitUserAsync` (the one place every admission path already flows through), and enrich `UserActionRecord` with a `ChatName` via a `LeftJoin` on `managed_chats` in the user-detail query.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9, EF Core 10 + Npgsql, NUnit, NSubstitute 6, bUnit (component tests), Testcontainers PostgreSQL (integration tests), Playwright (E2E).

**Spec:** `docs/superpowers/specs/2026-09-13-users-all-tab-and-action-chat-design.md`

## Global Constraints

- Branch: `fix/users-pending-tab-and-action-chat` (already created off `develop`). Never commit to `develop` or `master`. PR targets `develop`.
- Conventional commits (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`). Every commit message ends with the two trailer lines shown in Task 1 Step 6. Use `git commit -F- <<'EOF'` heredocs.
- One type per file. No tuples crossing method boundaries (existing `(List<T> Items, int TotalCount)` return is pre-existing and stays).
- No `[Obsolete]`, no backward-compat shims.
- NSubstitute matcher lambdas: `Arg.Is<T>(x => x!.Prop == y)` — use `!`, never `?.`.
- Integration tests need Docker (Testcontainers). Run only the targeted filters given in each task, not the whole integration suite.
- Do not touch the `Active`, `Tagged`, `Kicked`, or `Banned` predicates in the repository.
- `is_active` write semantics do not change; only where `ActivateAsync` is called moves.
- Do not create or edit any `appsettings*.json`.

---

### Task 1: `All` filter in the repository — models, query, counts, tests

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Models/UserListFilter.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Models/UserTabCounts.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Models/TelegramUserListItem.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs` (`GetPagedUsersAsync` ~434-532, `GetUserTabCountsAsync` ~636-680, `EnrichUserStatsAsync` ~722-737)
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs`

**Interfaces:**
- Consumes: existing `ITelegramUserRepository.GetPagedUsersAsync(UserListFilter filter, int skip, int take, string? searchText, List<long>? chatIds, string? sortLabel, bool sortDescending, CancellationToken)` and `GetUserTabCountsAsync(List<long>? chatIds, string? searchText, CancellationToken)`; `GetOrCreateAsync(UserIdentity, bool isBot, CancellationToken)` (inserts `is_active = false`); `SetBanStatusAsync(long, bool isBanned, DateTimeOffset? expiresAt, CancellationToken)`.
- Produces: `UserListFilter.All`; `UserTabCounts.AllCount` (int); `TelegramUserListItem.IsActive` (bool). Task 3 (Users page) relies on all three.

- [ ] **Step 1: Write the failing integration tests**

Append inside the class in `TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs`, before the final `}`:

```csharp
    #region All Filter Tests

    private async Task<long> SeedInactiveUserAsync(string username, string firstName)
    {
        // GetOrCreateAsync mirrors the join path: inserts is_active = false.
        var userId = Random.Shared.NextInt64(100_000_000_000L, 999_999_999_999L);
        await _repository!.GetOrCreateAsync(new UserIdentity(userId, firstName, null, username), isBot: false);
        return userId;
    }

    private static readonly List<long> GlobalScope = [0L];

    [Test]
    public async Task GetPagedUsersAsync_All_ReturnsInactiveNonBannedUser()
    {
        var userId = await SeedInactiveUserAsync($"pending_{Guid.NewGuid():N}", "Pending");

        var (allItems, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);
        var (activeItems, _) = await _repository.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allItems.Select(i => i.TelegramUserId), Does.Contain(userId), "All must include a pending joiner");
            Assert.That(activeItems.Select(i => i.TelegramUserId), Does.Not.Contain(userId), "Active still excludes unverified users");
        }
    }

    [Test]
    public async Task GetPagedUsersAsync_All_ReturnsUserWithExpiredBanFlagStillSet()
    {
        // The gap no other tab covers: is_banned = true but the ban already expired.
        var userId = Random.Shared.NextInt64(100_000_000_000L, 999_999_999_999L);
        await SeedActiveUserAsync(userId, username: $"expired_{Guid.NewGuid():N}");
        await _repository!.SetBanStatusAsync(userId, isBanned: true, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        var (allItems, _) = await _repository.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);
        var (activeItems, _) = await _repository.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);
        var (bannedItems, _) = await _repository.GetPagedBannedUsersWithDetailsAsync(
            skip: 0, take: 5000, searchText: null, sortLabel: null, sortDescending: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allItems.Select(i => i.TelegramUserId), Does.Contain(userId), "All must include the expired-ban user");
            Assert.That(activeItems.Select(i => i.TelegramUserId), Does.Not.Contain(userId), "Active excludes is_banned rows");
            Assert.That(bannedItems.Select(i => i.TelegramUserId), Does.Not.Contain(userId), "Banned excludes expired bans");
        }
    }

    [Test]
    public async Task GetPagedUsersAsync_All_ExcludesSystemUser()
    {
        var (allItems, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);

        Assert.That(allItems.Select(i => i.TelegramUserId), Does.Not.Contain(0L));
    }

    [Test]
    public async Task GetPagedUsersAsync_All_ProjectsIsActive()
    {
        var inactiveId = await SeedInactiveUserAsync($"inactive_{Guid.NewGuid():N}", "Inactive");

        var (allItems, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);

        var inactive = allItems.Single(i => i.TelegramUserId == inactiveId);
        var canonicalActive = allItems.Single(i => i.TelegramUserId == TopHamAuthorId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(inactive.IsActive, Is.False);
            Assert.That(canonicalActive.IsActive, Is.True);
        }
    }

    [Test]
    public async Task GetPagedUsersAsync_All_SearchFindsInactiveUser()
    {
        var marker = Guid.NewGuid().ToString("N")[..12];
        var userId = await SeedInactiveUserAsync($"find_{marker}", "Findable");

        var (items, totalCount) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 50,
            searchText: marker, chatIds: GlobalScope, sortLabel: null, sortDescending: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(totalCount, Is.EqualTo(1));
            Assert.That(items.Single().TelegramUserId, Is.EqualTo(userId));
        }
    }

    [Test]
    public async Task GetUserTabCountsAsync_AllCount_EqualsNonSystemRowCount_UnderGlobalScope()
    {
        await SeedInactiveUserAsync($"count_{Guid.NewGuid():N}", "Counted");

        var counts = await _repository!.GetUserTabCountsAsync(chatIds: GlobalScope, searchText: null);

        int rowCount;
        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var ctx = await factory.CreateDbContextAsync();
            rowCount = await ctx.TelegramUsers.CountAsync(u => u.TelegramUserId != 0);
        }

        Assert.That(counts.AllCount, Is.EqualTo(rowCount));
    }

    [Test]
    public async Task GetPagedUsersAsync_All_RespectsChatScope()
    {
        // A pending joiner has no messages, so a chat-scoped admin must not see them.
        var userId = await SeedInactiveUserAsync($"scoped_{Guid.NewGuid():N}", "Scoped");

        var (items, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 5000,
            searchText: null, chatIds: new List<long> { GoldenDatasetConstants.Chats.MainChatId },
            sortLabel: null, sortDescending: false);

        Assert.That(items.Select(i => i.TelegramUserId), Does.Not.Contain(userId));
    }

    #endregion
```

- [ ] **Step 2: Run the new tests to verify they fail to compile**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests 2>&1 | grep -E "error CS" | head`
Expected: errors mentioning `UserListFilter` has no member `All`, `UserTabCounts` has no `AllCount`, `TelegramUserListItem` has no `IsActive`.

- [ ] **Step 3: Add the model members**

`TelegramGroupsAdmin.Telegram/Models/UserListFilter.cs`:

```csharp
namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Users page tab filters. <see cref="All"/> applies no status predicate and is the
/// guaranteed-visible view; the others are filtered projections of it.
/// </summary>
public enum UserListFilter { All, Active, Tagged, Trusted, Kicked }
```

`TelegramGroupsAdmin.Telegram/Models/UserTabCounts.cs` — add as the first property:

```csharp
    public int AllCount { get; set; }
```

`TelegramGroupsAdmin.Telegram/Models/TelegramUserListItem.cs` — add after `IsTrusted`:

```csharp
    /// <summary>False until the user passes the join gate (welcome / exam / profile review) or posts a message.</summary>
    public bool IsActive { get; set; }
```

- [ ] **Step 4: Implement `All` in the repository**

In `GetPagedUsersAsync`, add a case to the `switch (filter)`:

```csharp
            case UiModels.UserListFilter.All:
                // No status predicate — the guaranteed-visible view. Base filter (system user),
                // chat scope, and search still apply below.
                break;
```

In the same method's `.Select(u => new UiModels.TelegramUserListItem { ... })` projection, add after `IsTrusted = u.IsTrusted,`:

```csharp
                IsActive = u.IsActive,
```

In `GetUserTabCountsAsync`, add before `var activeCount`:

```csharp
        var allCount = await baseQuery.CountAsync(cancellationToken);
```

and in the returned object add `AllCount = allCount,` as the first initializer. Update the comment "Run 5 count queries" to "Run 6 count queries".

`EnrichUserStatsAsync`: no change needed — the banned shortcut only triggers for `Active`/`Kicked`, so `All` already performs the banned lookup. Confirm the `if (filter is UiModels.UserListFilter.Active or UiModels.UserListFilter.Kicked)` line reads exactly that and leave it.

- [ ] **Step 5: Run the new tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TelegramUserRepositoryTests.GetPagedUsersAsync_All|FullyQualifiedName~TelegramUserRepositoryTests.GetUserTabCountsAsync_AllCount"`
Expected: 7 passed.

Also run the existing paged tests to confirm no regression:
`dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TelegramUserRepositoryTests"`
Expected: all passed.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Models/UserListFilter.cs TelegramGroupsAdmin.Telegram/Models/UserTabCounts.cs TelegramGroupsAdmin.Telegram/Models/TelegramUserListItem.cs TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs
git commit -F- <<'EOF'
feat(users): add predicate-free All filter and IsActive projection

Every other Users tab is a predicate that can exclude a row; All applies
only the system-user exclusion, chat scope, and search.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01TGukJYySot6Y6hrMrX7eE7
EOF
```

---

### Task 2: Trusted tab no longer requires `is_active`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs` (`case UiModels.UserListFilter.Trusted` ~457-459; `trustedCount` ~667)
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs`

**Interfaces:**
- Consumes: `ITelegramUserRepository.TrustUserAsync(long telegramUserId, CancellationToken)`; `SeedInactiveUserAsync` helper from Task 1.
- Produces: nothing new.

- [ ] **Step 1: Write the failing test**

Add inside the `#region All Filter Tests` block from Task 1:

```csharp
    [Test]
    public async Task GetPagedUsersAsync_Trusted_IncludesInactiveTrustedUser()
    {
        var userId = await SeedInactiveUserAsync($"trusted_{Guid.NewGuid():N}", "Trusted");
        await _repository!.TrustUserAsync(userId);

        var (items, _) = await _repository.GetPagedUsersAsync(
            UiModels.UserListFilter.Trusted, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);
        var counts = await _repository.GetUserTabCountsAsync(chatIds: GlobalScope, searchText: null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items.Select(i => i.TelegramUserId), Does.Contain(userId), "Trust is global; gate state must not hide it");
            Assert.That(counts.TrustedCount, Is.EqualTo(items.Count), "count must match the listed rows");
        }
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GetPagedUsersAsync_Trusted_IncludesInactiveTrustedUser"`
Expected: FAIL — the seeded user is not in `items`.

- [ ] **Step 3: Drop the `IsActive` condition**

In `GetPagedUsersAsync`:

```csharp
            case UiModels.UserListFilter.Trusted:
                query = query.Where(u => u.IsTrusted);
                break;
```

In `GetUserTabCountsAsync`:

```csharp
        var trustedCount = await baseQuery.Where(u => u.IsTrusted).CountAsync(cancellationToken);
```

- [ ] **Step 4: Run the test and the fixture**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TelegramUserRepositoryTests"`
Expected: all passed.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs
git commit -F- <<'EOF'
fix(users): list trusted users on the Trusted tab regardless of join-gate state

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01TGukJYySot6Y6hrMrX7eE7
EOF
```

---

### Task 3: All tab on the Users page

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Pages/Users.razor` (tabs start ~line 40; refs ~292-296; `LoadXServerDataAsync` ~328-339; `ReloadAllTablesAsync` ~517-524; Kicked description ~245-247)
- Test: `TelegramGroupsAdmin.E2ETests/Tests/Users/UsersTests.cs` (`Users_HasExpectedTabs` ~104-128)

**Interfaces:**
- Consumes: `UserListFilter.All`, `UserTabCounts.AllCount`, `TelegramUserListItem.IsActive` (Task 1); existing page members `LoadUsersServerDataAsync(UserListFilter, TableState, CancellationToken)`, `ViewUserDetail(long)`, `ToggleTrust(TelegramUserListItem)`, `GetStatusColor`, `GetStatusIcon`.
- Produces: `_allTable` field; `LoadAllServerDataAsync`.

- [ ] **Step 1: Add the E2E assertion (fails until the tab exists)**

In `Users_HasExpectedTabs`, add as the first assertion inside `Assert.EnterMultipleScope()`:

```csharp
            Assert.That(tabNames.FirstOrDefault()?.Contains("ALL", StringComparison.OrdinalIgnoreCase), Is.True,
                "'All' should be the first tab");
```

Do not run the E2E suite now (it needs the full app + browser); it is verified in Task 7.

- [ ] **Step 2: Add the All tab panel**

In `Users.razor`, insert immediately after `<MudTabs Elevation="2" Rounded="true" ApplyEffectsToContainer="true" TabPanelsClass="pa-6">` and before the existing `<MudTabPanel Text="Active" ...>`:

```razor
        <MudTabPanel Text="All" Icon="@Icons.Material.Filled.Groups" BadgeData="@_tabCounts.AllCount" BadgeColor="Color.Default">
            <MudText Typo="Typo.body2" Class="mb-4" Color="Color.Secondary">
                Every user the bot has seen in your groups. Use this tab when you cannot find someone elsewhere.
            </MudText>
            <MudTable T="TelegramUserListItem"
                     @ref="_allTable"
                     ServerData="@LoadAllServerDataAsync"
                     Hover="true" Breakpoint="Breakpoint.Sm" Dense="true">
                <HeaderContent>
                    <MudTh><MudTableSortLabel T="TelegramUserListItem" SortLabel="User">User</MudTableSortLabel></MudTh>
                    <MudTh><MudTableSortLabel T="TelegramUserListItem" SortLabel="Status">Status</MudTableSortLabel></MudTh>
                    <MudTh><MudTableSortLabel T="TelegramUserListItem" SortLabel="LastSeen" InitialDirection="SortDirection.Descending">Last Seen</MudTableSortLabel></MudTh>
                    <MudTh>Actions</MudTh>
                </HeaderContent>
                <RowTemplate>
                    <MudTd DataLabel="User">
                        <UserInfoCell User="context" />
                    </MudTd>
                    <MudTd DataLabel="Status">
                        <MudStack Row="true" Spacing="1" AlignItems="AlignItems.Center">
                            <MudChip T="string" Size="Size.Small" Color="@GetStatusColor(context.Status)">
                                @GetStatusIcon(context.Status) @context.Status.ToString()
                            </MudChip>
                            @if (!context.IsActive && !context.IsBanned)
                            {
                                <MudTooltip Text="Has not passed welcome verification, the entrance exam, or profile review">
                                    <MudChip T="string" Size="Size.Small" Variant="Variant.Outlined" Color="Color.Default">Unverified</MudChip>
                                </MudTooltip>
                            }
                        </MudStack>
                    </MudTd>
                    <MudTd DataLabel="Last Seen">
                        <LocalTimestamp Value="@context.LastSeenAt" />
                    </MudTd>
                    <MudTd DataLabel="Actions">
                        <MudTooltip Text="View Details">
                            <MudIconButton Icon="@Icons.Material.Filled.Visibility" Size="Size.Small" OnClick="@(() => ViewUserDetail(context.TelegramUserId))" />
                        </MudTooltip>
                        <MudTooltip Text="@(context.IsTrusted ? "Remove Trust" : "Trust User")">
                            <MudIconButton Icon="@(context.IsTrusted ? Icons.Material.Filled.PersonOff : Icons.Material.Filled.VerifiedUser)"
                                          Size="Size.Small"
                                          Color="@(context.IsTrusted ? Color.Default : Color.Success)"
                                          OnClick="@(() => ToggleTrust(context))" />
                        </MudTooltip>
                    </MudTd>
                </RowTemplate>
                <PagerContent>
                    <MudTablePager PageSizeOptions="[25, 50, 100]" />
                </PagerContent>
            </MudTable>
        </MudTabPanel>
```

Initial sort is set with `InitialDirection="SortDirection.Descending"` on the Last Seen `MudTableSortLabel` — the same pattern the Banned tab already uses for its `BannedAt` column (`Users.razor` ~line 175). With `ServerData`, MudBlazor passes that as `TableState.SortLabel = "LastSeen"` / `SortDirection.Descending` on first load, which `GetPagedUsersAsync` already handles.

- [ ] **Step 3: Wire the table ref, loader, and reload**

Table refs block — add as the first line:

```csharp
    private MudTable<TelegramUserListItem> _allTable = null!;
```

ServerData callbacks — add before `LoadActiveServerDataAsync`:

```csharp
    private Task<TableData<TelegramUserListItem>> LoadAllServerDataAsync(TableState state, CancellationToken ct)
        => LoadUsersServerDataAsync(UserListFilter.All, state, ct);
```

`ReloadAllTablesAsync` — add as the first line inside the method:

```csharp
        if (_allTable != null) await _allTable.ReloadServerData();
```

- [ ] **Step 4: Reword the Kicked description**

Replace the Kicked panel's description text:

```razor
            <MudText Typo="Typo.body2" Class="mb-4" Color="Color.Secondary">
                Users who have not passed the join gate and are not banned — welcome timeouts, declined rules, left before verifying, admin kicks, and users still waiting to verify.
            </MudText>
```

- [ ] **Step 5: Build**

Run: `dotnet build TelegramGroupsAdmin 2>&1 | grep -E "error|Warn.*Users.razor" | head`
Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin/Components/Pages/Users.razor TelegramGroupsAdmin.E2ETests/Tests/Users/UsersTests.cs
git commit -F- <<'EOF'
feat(users): add All tab as the default guaranteed-visible view

Shows every user in scope with an Unverified chip for rows that have not
passed the join gate. Kicked description now says what that tab holds.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01TGukJYySot6Y6hrMrX7eE7
EOF
```

---

### Task 4: Activate users inside `WelcomeAdmissionHandler`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeAdmissionHandler.cs` (~62-77)
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Welcome/WelcomeAdmissionHandlerTests.cs`

**Interfaces:**
- Consumes: `ITelegramUserRepository.ActivateAsync(long telegramUserId, CancellationToken)`; `IBotModerationService.RestoreUserPermissionsAsync(RestorePermissionsIntent, CancellationToken)`.
- Produces: `TryAdmitUserAsync` now activates the user on every `Admitted` return. Task 5 removes the callers' own activation on the strength of this.

- [ ] **Step 1: Register the repo mock in the test fixture and write failing tests**

In `WelcomeAdmissionHandlerTests`, add a field after `_moderationService`:

```csharp
    private ITelegramUserRepository _telegramUserRepo = null!;
```

In `SetUp`, after `_moderationService = Substitute.For<IBotModerationService>();` add:

```csharp
        _telegramUserRepo = Substitute.For<ITelegramUserRepository>();
```

and after `services.AddSingleton(_moderationService);` add:

```csharp
        services.AddSingleton(_telegramUserRepo);
```

Add a new region before the closing `}` of the class:

```csharp
    #region Activation

    [Test]
    public async Task TryAdmitUserAsync_Admitted_ActivatesUser()
    {
        _reportsRepo
            .HasPendingProfileScanAlertAsync(TestUserId, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _welcomeRepo
            .GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns(CreateWelcomeResponse(WelcomeResponseType.Accepted));
        _moderationService
            .RestoreUserPermissionsAsync(Arg.Any<RestorePermissionsIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        var result = await _handler.TryAdmitUserAsync(
            TestUser, TestChat, TestExecutor, TestReason, CancellationToken.None);

        Assert.That(result, Is.EqualTo(AdmissionResult.Admitted));
        await _telegramUserRepo.Received(1).ActivateAsync(TestUserId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryAdmitUserAsync_ProfileHold_DoesNotActivate()
    {
        _reportsRepo
            .HasPendingProfileScanAlertAsync(TestUserId, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _handler.TryAdmitUserAsync(
            TestUser, TestChat, TestExecutor, TestReason, CancellationToken.None);

        Assert.That(result, Is.EqualTo(AdmissionResult.StillWaiting));
        await _telegramUserRepo.DidNotReceive().ActivateAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryAdmitUserAsync_WelcomePending_DoesNotActivate()
    {
        _reportsRepo
            .HasPendingProfileScanAlertAsync(TestUserId, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _welcomeRepo
            .GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns(CreateWelcomeResponse(WelcomeResponseType.Pending));

        var result = await _handler.TryAdmitUserAsync(
            TestUser, TestChat, TestExecutor, TestReason, CancellationToken.None);

        Assert.That(result, Is.EqualTo(AdmissionResult.StillWaiting));
        await _telegramUserRepo.DidNotReceive().ActivateAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    #endregion
```

`CreateWelcomeResponse(WelcomeResponseType)` already exists in this fixture (used by the existing gate tests); reuse it.

- [ ] **Step 2: Run to verify the first test fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeAdmissionHandlerTests"`
Expected: `TryAdmitUserAsync_Admitted_ActivatesUser` FAILS (`Expected to receive exactly 1 call ... Actually received no matching calls`); the two `DoesNotActivate` tests pass; all pre-existing tests pass.

- [ ] **Step 3: Activate in the handler**

In `WelcomeAdmissionHandler.TryAdmitUserAsync`, replace the block from `await moderationService.RestoreUserPermissionsAsync(intent, ct);` through `return AdmissionResult.Admitted;` with:

```csharp
        await moderationService.RestoreUserPermissionsAsync(intent, ct);

        // Single place where a user crosses the join gate. Every admission path (group welcome,
        // DM welcome, exam pass, profile-scan allow) flows through here, so callers must not
        // activate on their own.
        var telegramUserRepo = sp.GetRequiredService<ITelegramUserRepository>();
        await telegramUserRepo.ActivateAsync(user.Id, ct);

        logger.LogInformation("Admission: {User} admitted to {Chat} — all gates clear ({Reason})",
            user.ToLogInfo(), chat.ToLogInfo(), reason);

        return AdmissionResult.Admitted;
```

Update the class XML summary's gate list to end with: `User is admitted only when ALL gates are clear; admission restores permissions and marks the user active.`

- [ ] **Step 4: Run the fixture**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeAdmissionHandlerTests"`
Expected: all passed (8 tests).

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeAdmissionHandler.cs TelegramGroupsAdmin.UnitTests/Telegram/Services/Welcome/WelcomeAdmissionHandlerTests.cs
git commit -F- <<'EOF'
fix(welcome): activate users inside the admission handler

Profile-scan Allow admitted users without ever marking them active; every
admission path now activates in the one place they all flow through.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01TGukJYySot6Y6hrMrX7eE7
EOF
```

---

### Task 5: Remove the duplicated activation calls from callers

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs` (~458-462, ~1098-1101, ~1264-1267)
- Modify: `TelegramGroupsAdmin.Telegram/Services/ExamFlowService.cs` (~741, ~778-779)
- Test (verify only): `TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs`, `TelegramGroupsAdmin.UnitTests/Telegram/Services/ExamFlowServiceTests.cs`

**Interfaces:**
- Consumes: Task 4's guarantee that `TryAdmitUserAsync` activates on `Admitted`.
- Produces: nothing new.

- [ ] **Step 1: Remove the three `WelcomeService` calls that follow an `Admitted` check**

Site A (~line 461, "security passed, welcome disabled"). Change:

```csharp
                if (admissionResult == AdmissionResult.Admitted)
                {
                    // Mark user as active (security passed, no welcome flow)
                    await telegramUserRepository.ActivateAsync(user.Id, cancellationToken);

                    // Delete verifying message
```

to:

```csharp
                if (admissionResult == AdmissionResult.Admitted)
                {
                    // Delete verifying message
```

Site B (~line 1100, group welcome completed). Change:

```csharp
        if (admissionResult == AdmissionResult.Admitted)
        {
            await telegramUserRepository.ActivateAsync(user.Id, cancellationToken);
            await TryDeleteMessageAsync(chat.Id, welcomeMessageId, cancellationToken);
```

to:

```csharp
        if (admissionResult == AdmissionResult.Admitted)
        {
            await TryDeleteMessageAsync(chat.Id, welcomeMessageId, cancellationToken);
```

Site C (~line 1266, DM welcome completed). Change:

```csharp
        if (admissionResult == AdmissionResult.Admitted)
        {
            await telegramUserRepository.ActivateAsync(user.Id, cancellationToken);
            await TryDeleteMessageAsync(groupChatId, welcomeResponse.WelcomeMessageId, cancellationToken);
```

to:

```csharp
        if (admissionResult == AdmissionResult.Admitted)
        {
            await TryDeleteMessageAsync(groupChatId, welcomeResponse.WelcomeMessageId, cancellationToken);
```

**Keep** the `ActivateAsync` at ~line 174 inside the `bypassDecision != BypassDecision.None` block — bypass never calls the admission handler.

Confirm with `grep -n "ActivateAsync" TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs` → exactly one hit (the bypass site).

- [ ] **Step 2: Remove the `ExamFlowService` call and its now-unused scoped resolve**

Around line 741 delete:

```csharp
        var telegramUserRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
```

Around line 778-779 delete the comment and call:

```csharp
            // 4. Mark user as active
            await telegramUserRepo.ActivateAsync(user.Id, cancellationToken);
```

and renumber the following comment `// 5. Send success DM ...` to `// 4. Send success DM ...`.

Confirm with `grep -n "ITelegramUserRepository\|telegramUserRepo" TelegramGroupsAdmin.Telegram/Services/ExamFlowService.cs` → no hits. Then check whether `using TelegramGroupsAdmin.Telegram.Repositories;` still resolves anything else in the file (`grep -n "Repository" TelegramGroupsAdmin.Telegram/Services/ExamFlowService.cs`); if `IWelcomeResponsesRepository` or another repository type is still referenced, keep the using, otherwise remove it.

- [ ] **Step 3: Build and run the affected unit fixtures**

Run: `dotnet build TelegramGroupsAdmin.Telegram 2>&1 | grep -E "error|warning CS|IDE0005" | head`
Expected: clean.

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeServiceTests|FullyQualifiedName~WelcomeServiceBlacklistTests|FullyQualifiedName~ExamFlowServiceTests|FullyQualifiedName~ProfileScanHandlerTests"`
Expected: all passed. The only `ActivateAsync` assertion in `WelcomeServiceTests` (~line 575) is on the bypass path and must still pass. If any test asserts `Received().ActivateAsync` for a completion path, delete that assertion — the behaviour now lives in the admission handler and is tested there (Task 4).

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs TelegramGroupsAdmin.Telegram/Services/ExamFlowService.cs TelegramGroupsAdmin.UnitTests
git commit -F- <<'EOF'
refactor(welcome): drop per-caller ActivateAsync now handled by admission

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01TGukJYySot6Y6hrMrX7eE7
EOF
```

---

### Task 6: `ChatName` on `UserActionRecord` from the user-detail query

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Models/UserActionRecord.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/Mappings/UserActionMappings.cs` (`ToModel` ~26-46)
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs` (`GetUserDetailAsync` actions query ~857-875 and the `Actions = ...` mapping ~948-955)
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs`

**Interfaces:**
- Consumes: `context.ManagedChats` (`ManagedChatRecordDto.ChatId`, `.ChatName`), `context.UserActions` (`UserActionRecordDto`).
- Produces: `UserActionRecord.ChatName` (string?, optional trailing positional parameter, default null). Task 7 renders it.

- [ ] **Step 1: Write the failing integration test**

Add to `TelegramUserRepositoryTests` in a new region:

```csharp
    #region User Detail Action Chat Name

    [Test]
    public async Task GetUserDetailAsync_Actions_IncludeChatNameForManagedChat_AndNullForUnknownChat()
    {
        var userId = Random.Shared.NextInt64(100_000_000_000L, 999_999_999_999L);
        await SeedActiveUserAsync(userId, username: $"actions_{Guid.NewGuid():N}");
        const long unknownChatId = -100_099_999_999_999L;

        string? mainChatName;
        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var ctx = await factory.CreateDbContextAsync();
            mainChatName = await ctx.ManagedChats
                .Where(c => c.ChatId == GoldenDatasetConstants.Chats.MainChatId)
                .Select(c => c.ChatName)
                .SingleAsync();

            var now = DateTimeOffset.UtcNow;
            ctx.UserActions.AddRange(
                new TelegramGroupsAdmin.Data.Models.UserActionRecordDto
                {
                    UserId = userId,
                    ActionType = (int)UserActionType.RestorePermissions,
                    ChatId = GoldenDatasetConstants.Chats.MainChatId,
                    SystemIdentifier = "WelcomeFlow",
                    IssuedAt = now.AddMinutes(-2),
                    Reason = "Completed welcome/rules flow"
                },
                new TelegramGroupsAdmin.Data.Models.UserActionRecordDto
                {
                    UserId = userId,
                    ActionType = (int)UserActionType.Kick,
                    ChatId = unknownChatId,
                    SystemIdentifier = "WelcomeFlow",
                    IssuedAt = now.AddMinutes(-1),
                    Reason = "Welcome timeout"
                },
                new TelegramGroupsAdmin.Data.Models.UserActionRecordDto
                {
                    UserId = userId,
                    ActionType = (int)UserActionType.Trust,
                    ChatId = null,
                    SystemIdentifier = "AutoTrust",
                    IssuedAt = now,
                    Reason = "Global action"
                });
            await ctx.SaveChangesAsync();
        }

        var detail = await _repository!.GetUserDetailAsync(userId);

        Assert.That(detail, Is.Not.Null);
        var byType = detail!.Actions.ToDictionary(a => a.ActionType);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mainChatName, Is.Not.Null.And.Not.Empty, "golden dataset main chat must have a name");
            Assert.That(byType[UserActionType.RestorePermissions].ChatName, Is.EqualTo(mainChatName));
            Assert.That(byType[UserActionType.Kick].ChatId, Is.EqualTo(unknownChatId));
            Assert.That(byType[UserActionType.Kick].ChatName, Is.Null, "unmanaged chat has no name to show");
            Assert.That(byType[UserActionType.Trust].ChatId, Is.Null);
            Assert.That(byType[UserActionType.Trust].ChatName, Is.Null);
        }
    }

    #endregion
```

If `UserActionRecordDto` has additional required (non-nullable, no default) members, set them to the obvious neutral value (`MessageId = null`, `ExpiresAt = null`, `WebUserId = null`, `TelegramUserId = null`). Read the DTO in `TelegramGroupsAdmin.Data/Models/UserActionRecordDto.cs` before writing the object initializers.

- [ ] **Step 2: Verify it fails to compile**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests 2>&1 | grep -E "error CS" | head -3`
Expected: `'UserActionRecord' does not contain a definition for 'ChatName'`.

- [ ] **Step 3: Add `ChatName` to the record and mapping**

`UserActionRecord.cs` — add a final positional parameter after `TargetLastName`:

```csharp
    string? TargetLastName = null,
    // Chat display name resolved from managed_chats (null when ChatId is null or the chat is no longer managed)
    string? ChatName = null
```

`UserActionMappings.ToModel` — add parameter and pass-through:

```csharp
        /// <param name="chatName">Managed chat name for ChatId (from managed_chats LEFT JOIN)</param>
        public UiModels.UserActionRecord ToModel(
            string? webUserEmail = null,
            string? telegramUsername = null,
            string? telegramFirstName = null,
            string? telegramLastName = null,
            string? targetUsername = null,
            string? targetFirstName = null,
            string? targetLastName = null,
            string? chatName = null) => new(
            Id: data.Id,
            UserId: data.UserId,
            ActionType: (UserActionType)data.ActionType,
            MessageId: data.MessageId,
            ChatId: data.ChatId,
            IssuedBy: ActorMappings.ToActor(data.WebUserId, data.TelegramUserId, data.SystemIdentifier, webUserEmail, telegramUsername, telegramFirstName, telegramLastName),
            IssuedAt: data.IssuedAt,
            ExpiresAt: data.ExpiresAt,
            Reason: data.Reason,
            TargetUsername: targetUsername,
            TargetFirstName: targetFirstName,
            TargetLastName: targetLastName,
            ChatName: chatName
        );
```

(`ToDto` does not change — `ChatName` is derived, never persisted.)

- [ ] **Step 4: Join `managed_chats` in `GetUserDetailAsync`**

Replace the actions query with:

```csharp
        // Get user actions with actor display name enrichment (LEFT JOINs for IssuedBy) and chat name
        var actions = await context.UserActions
            .AsNoTracking()
            .Where(ua => ua.UserId == telegramUserId)
            .LeftJoin(context.TelegramUsers, ua => ua.TelegramUserId, tu => tu.TelegramUserId, (ua, ta) => new { ua, ta })
            .LeftJoin(context.Users, x => x.ua.WebUserId, wu => wu.Id, (x, wa) => new { x.ua, x.ta, wa })
            .LeftJoin(context.TelegramUsers, x => x.ua.UserId, t => t.TelegramUserId, (x, t) => new { x.ua, x.ta, x.wa, t })
            .LeftJoin(context.ManagedChats, x => x.ua.ChatId, c => (long?)c.ChatId, (x, c) => new
            {
                Action = x.ua,
                TelegramActorUsername = x.ta != null ? x.ta.Username : null,
                TelegramActorFirstName = x.ta != null ? x.ta.FirstName : null,
                TelegramActorLastName = x.ta != null ? x.ta.LastName : null,
                WebActorEmail = x.wa != null ? x.wa.Email : null,
                TargetUsername = x.t != null ? x.t.Username : null,
                TargetFirstName = x.t != null ? x.t.FirstName : null,
                TargetLastName = x.t != null ? x.t.LastName : null,
                ChatName = c != null ? c.ChatName : null
            })
            .OrderByDescending(x => x.Action.IssuedAt)
            .ToListAsync(cancellationToken);
```

The `(long?)c.ChatId` cast makes both join keys `long?` so the `LeftJoin` overload type-checks against the nullable `ua.ChatId`. If EF rejects the cast form, use the query-syntax equivalent already used for chat memberships in the same method (`join c in context.ManagedChats on x.ua.ChatId equals (long?)c.ChatId into g from c in g.DefaultIfEmpty()`).

Then in the returned `TelegramUserDetail`, extend the `Actions` mapping:

```csharp
            Actions = actions.Select(a => a.Action.ToModel(
                webUserEmail: a.WebActorEmail,
                telegramUsername: a.TelegramActorUsername,
                telegramFirstName: a.TelegramActorFirstName,
                telegramLastName: a.TelegramActorLastName,
                targetUsername: a.TargetUsername,
                targetFirstName: a.TargetFirstName,
                targetLastName: a.TargetLastName,
                chatName: a.ChatName)).ToList(),
```

- [ ] **Step 5: Run the test**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GetUserDetailAsync_Actions_IncludeChatName"`
Expected: PASS.

Also: `dotnet build 2>&1 | grep -E "error" | head` — every `new UserActionRecord(...)` call site (AuditHandler, UserAutoTrustService, TelegramUserManagementService, BotChatService, MessageProcessingService) must still compile; they do because the new parameter is optional and last.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Models/UserActionRecord.cs TelegramGroupsAdmin.Telegram/Repositories/Mappings/UserActionMappings.cs TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs
git commit -F- <<'EOF'
feat(users): resolve chat name for action history entries

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01TGukJYySot6Y6hrMrX7eE7
EOF
```

---

### Task 7: Render the chat in the action-history timeline

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/UserDetailDialog.razor` (timeline item content ~395-409)
- Test: `TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogTests.cs`

**Interfaces:**
- Consumes: `UserActionRecord.ChatId` (long?), `UserActionRecord.ChatName` (string?) from Task 6.
- Produces: nothing new.

- [ ] **Step 1: Write the failing component tests**

In `UserDetailDialogTests`, add a helper next to `CreateUserDetail`:

```csharp
    private static UserActionRecord CreateAction(UserActionType type, long? chatId, string? chatName, string reason)
        => new(
            Id: Random.Shared.NextInt64(1, long.MaxValue),
            UserId: TestUserId,
            ActionType: type,
            MessageId: null,
            ChatId: chatId,
            IssuedBy: Actor.FromSystem("WelcomeFlow"),
            IssuedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt: null,
            Reason: reason,
            ChatName: chatName);
```

and a new region of tests (mirror the `SetUp`/render/open pattern used by `UserDetailDialogHistoryTests`):

```csharp
    #region Action History Chat Context

    [Test]
    public void ActionHistory_ShowsChatName_WhenActionHasManagedChat()
    {
        var detail = CreateUserDetail();
        detail.Actions = [CreateAction(UserActionType.RestorePermissions, -100_123L, "Weekenders Main", "Completed welcome/rules flow")];
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(detail));

        var provider = RenderDialogProvider();
        var dialogTask = OpenDialogAsync(TestUserId);

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Action History"));
            Assert.That(provider.Markup, Does.Contain("in Weekenders Main"));
        });
        Assert.That(dialogTask.Exception, Is.Null);
    }

    [Test]
    public void ActionHistory_ShowsChatId_WhenChatNameUnknown()
    {
        var detail = CreateUserDetail();
        detail.Actions = [CreateAction(UserActionType.Kick, -100_555L, null, "Welcome timeout")];
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(detail));

        var provider = RenderDialogProvider();
        var dialogTask = OpenDialogAsync(TestUserId);

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("in -100555"));
        });
        Assert.That(dialogTask.Exception, Is.Null);
    }

    [Test]
    public void ActionHistory_NoChatLine_WhenActionIsGlobal()
    {
        var detail = CreateUserDetail();
        detail.Actions = [CreateAction(UserActionType.Trust, null, null, "Manually trusted")];
        _mockUserService.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelegramUserDetail?>(detail));

        var provider = RenderDialogProvider();
        var dialogTask = OpenDialogAsync(TestUserId);

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Action History"));
            Assert.That(provider.Markup, Does.Not.Contain("action-chat-context"));
        });
        Assert.That(dialogTask.Exception, Is.Null);
    }

    #endregion
```

`UserDetailDialogTests` has no `[SetUp]` today, so `provider.Markup` accumulates dialogs across tests in the fixture and the `Does.Not.Contain` assertion above would see earlier tests' markup. Add this method after the constructor (it is the same one `UserDetailDialogHistoryTests` uses):

```csharp
    [SetUp]
    public async Task SetUp()
    {
        // Clear previously rendered components so each test starts with a fresh DOM.
        await DisposeComponentsAsync();
    }
```

Then run the whole fixture once (`dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~UserDetailDialogTests"`) before adding the new tests to confirm the existing tests still pass with a fresh DOM per test. If any existing test relied on leaked markup, fix that test's arrangement rather than dropping the `SetUp`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~UserDetailDialogTests.ActionHistory_"`
Expected: first two FAIL (markup lacks "in Weekenders Main" / "in -100555"); the third passes trivially for now.

- [ ] **Step 3: Render the chat line**

In `UserDetailDialog.razor`, inside the timeline `<MudStack Spacing="0" Class="ml-2" Style="flex: 1;">`, insert between the `@if (!string.IsNullOrEmpty(action.Reason)) { ... }` block and the `by @action.IssuedBy...` caption:

```razor
                                                @if (action.ChatId is not null)
                                                {
                                                    <MudText Typo="Typo.caption" Color="Color.Secondary" Class="action-chat-context">
                                                        in @(action.ChatName ?? action.ChatId.Value.ToString())
                                                    </MudText>
                                                }
```

- [ ] **Step 4: Run the component tests**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~UserDetailDialog"`
Expected: all passed, including the three new tests.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/UserDetailDialog.razor TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogTests.cs
git commit -F- <<'EOF'
feat(users): show which chat an action-history entry belongs to

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01TGukJYySot6Y6hrMrX7eE7
EOF
```

---

### Task 8: Full verification and PR

**Files:**
- None new. Verification only.

- [ ] **Step 1: Full build with warnings as signal**

Run: `dotnet build 2>&1 | grep -E "error|warning" | grep -v "NU1" | head -20`
Expected: no errors; no new warnings in files touched by this plan.

- [ ] **Step 2: Unit and component suites (allowed without asking)**

Run: `dotnet test TelegramGroupsAdmin.UnitTests 2>&1 | tail -5`
Expected: `Passed!` with 0 failed.

Run: `dotnet test TelegramGroupsAdmin.ComponentTests 2>&1 | tail -5`
Expected: `Passed!` with 0 failed.

- [ ] **Step 3: Targeted integration fixture**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TelegramUserRepositoryTests" 2>&1 | tail -5`
Expected: `Passed!` with 0 failed. Do not run the full integration or E2E suites without Kass's go-ahead (see context-keep memory `gate_heavy_suites_when_parallel_sessions`); report that the E2E assertion added in Task 3 is pending a suite run.

- [ ] **Step 4: Confirm the spec invariants against the code**

- `grep -n "ActivateAsync" TelegramGroupsAdmin.Telegram/Services/*.cs TelegramGroupsAdmin.Telegram/Services/**/*.cs` → exactly two hits: `WelcomeAdmissionHandler.cs` and the bypass site in `WelcomeService.cs`.
- `grep -n "case UiModels.UserListFilter" TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs` → `All`, `Active`, `Tagged`, `Trusted`, `Kicked`; `Active`, `Tagged`, `Kicked` bodies unchanged from `develop` (`git diff develop -- TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs`).

- [ ] **Step 5: Push and open the PR to `develop`**

```bash
git push -u origin fix/users-pending-tab-and-action-chat
gh pr create --base develop --title "fix(users): All tab guarantees every user is visible; action history shows chat" --body-file - <<'EOF'
## Summary

Two long-standing Users page bugs.

**Newly joined users were invisible.** `is_active` means "passed the join gate" and is never cleared, but the tabs read it as "not kicked". Pending joiners were hidden from Active and misfiled under Kicked; a user with an expired-but-uncleared temp ban appeared on no tab at all. A new **All** tab (first, default) applies no status predicate, so every user in scope is findable, with an "Unverified" chip for rows that have not passed the gate. The Trusted tab no longer requires `is_active`.

**Profile-scan Allow never activated the user.** Activation now happens once inside `WelcomeAdmissionHandler.TryAdmitUserAsync`; the four duplicated calls in `WelcomeService` / `ExamFlowService` are removed.

**Action history now says which chat.** `UserActionRecord.ChatName` is resolved via `managed_chats` in the detail query and rendered as "in {chat}" (falls back to the chat id).

Spec: `docs/superpowers/specs/2026-09-13-users-all-tab-and-action-chat-design.md`
Plan: `docs/superpowers/plans/2026-09-13-users-all-tab-and-action-chat.md`

## Testing

- Integration: `TelegramUserRepositoryTests` — All filter (7 tests), Trusted-without-is_active, action chat name join.
- Unit: `WelcomeAdmissionHandlerTests` — activation on Admitted, none on StillWaiting.
- Component: `UserDetailDialogTests` — chat line with name / id / absent.
- E2E: `Users_HasExpectedTabs` asserts All is first (pending a suite run).

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01TGukJYySot6Y6hrMrX7eE7
EOF
```

No `Closes #N` line: no issue exists for these bugs. If Kass files one, add it to the top of the body.
