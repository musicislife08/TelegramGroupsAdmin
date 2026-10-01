using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects.Settings;
using TelegramGroupsAdmin.Telegram.Repositories;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Settings;

/// <summary>
/// Ban Celebration settings on canonical data as it is, as the canonical Owner: the caption and
/// GIF libraries are the canonical rows (<c>06_ban_celebration_captions.sql</c>,
/// <c>07_ban_celebration_gifs.sql</c>), and the add-caption, upload and add-from-URL writes are the
/// assertion subjects. Counts are read from this test's clone at runtime.
/// </summary>
/// <remarks>
/// Duplicate detection compares the uploaded file's average hash (8 bytes, one bit per 8x8 luminance
/// cell above the mean) against the <c>photo_hash</c> column within a Hamming distance of 8; the
/// canonical media files themselves are never read, so their absence from this app instance's data
/// path does not matter. The fixture GIF is a single solid-colour frame, so every cell equals the
/// mean and its hash is all zero bits. <see cref="ArrangeDataAsync"/> pins that no canonical hash is
/// within the threshold of that (canonical 2026-10-01: the nearest has 16 bits set), so the first
/// upload is accepted and only the second, identical one trips the warning.
/// </remarks>
[TestFixture]
public class BanCelebrationGoldenTests : GoldenE2ETestBase
{
    /// <summary>Hamming distance at or under which the Add GIF dialog reports a similar GIF.</summary>
    private const int DuplicateThresholdBits = 8;

    private BanCelebrationSettingsPage _page = null!;
    private WireMockServer? _gifHost;
    private readonly List<string> _fixtureFiles = [];
    private int _canonicalGifCount;
    private int _canonicalCaptionCount;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        _canonicalGifCount = await context.BanCelebrationGifs.CountAsync();
        _canonicalCaptionCount = await context.BanCelebrationCaptions.CountAsync();
        Assert.That(_canonicalGifCount, Is.GreaterThan(0), "canonical must hold GIFs");
        Assert.That(_canonicalCaptionCount, Is.GreaterThan(0), "canonical must hold captions");

        // The fixture's hash is all zeros, so its distance to a stored hash is that hash's bit count.
        var hashes = await context.BanCelebrationGifs.AsNoTracking()
            .Where(g => g.PhotoHash != null)
            .Select(g => g.PhotoHash!)
            .ToListAsync();
        Assert.That(hashes, Is.Not.Empty, "canonical GIFs must carry photo hashes for duplicate detection");
        var nearestBits = hashes.Min(h => h.Sum(b => System.Numerics.BitOperations.PopCount(b)));
        Assert.That(nearestBits, Is.GreaterThan(DuplicateThresholdBits),
            "no canonical GIF may be a near-duplicate of the solid-colour fixture, or the first upload would be flagged");
    }

    [SetUp]
    public async Task LoginAsOwner()
    {
        _page = new BanCelebrationSettingsPage(Page);
        await LoginAsOwnerAsync();
        await _page.NavigateAsync();
        await _page.WaitForLoadAsync();
    }

    [TearDown]
    public void StopGifHostAndDeleteFixtures()
    {
        _gifHost?.Stop();
        _gifHost?.Dispose();
        _gifHost = null;

        foreach (var file in _fixtureFiles.Where(File.Exists))
        {
            File.Delete(file);
        }
        _fixtureFiles.Clear();
    }

    #region Captions

    [Test]
    public async Task AddCaption_AppearsInTheLibraryAndIsStored()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var name = $"e2e synthetic caption {suffix}";
        var chatText = $"e2e synthetic chat text {suffix} for {{username}}";
        var dmText = $"e2e synthetic dm text {suffix} for you";
        await Expect(_page.CaptionRows).ToHaveCountAsync(_canonicalCaptionCount);

        await _page.OpenAddCaptionDialogAsync();
        await Expect(_page.SubmitCaptionButtonLocator).ToBeDisabledAsync();
        await _page.FillCaptionDialogAsync(name, chatText, dmText);
        await Expect(_page.SubmitCaptionButtonLocator).ToBeEnabledAsync();
        await _page.SubmitCaptionAndWaitForCloseAsync();

        await Expect(_page.SnackbarWithText("Caption added successfully")).ToBeVisibleAsync();
        await Expect(_page.CaptionRow(chatText)).ToHaveCountAsync(1);
        await Expect(_page.CaptionRow(chatText)).ToContainTextAsync(name);
        await Expect(_page.CaptionRow(chatText)).ToContainTextAsync(dmText);
        await Expect(_page.CaptionRows).ToHaveCountAsync(_canonicalCaptionCount + 1);

        await using var ctx = CreateDbContext();
        var stored = await ctx.BanCelebrationCaptions.AsNoTracking()
            .Where(c => c.Text == chatText)
            .Select(c => new { c.Name, c.DmText, c.DispensedAt }).SingleAsync();
        Assert.That(stored.Name, Is.EqualTo(name));
        Assert.That(stored.DmText, Is.EqualTo(dmText));
        Assert.That(stored.DispensedAt, Is.Null, "a new caption has not been dispensed in the current rotation");
        Assert.That(await ctx.BanCelebrationCaptions.CountAsync(), Is.EqualTo(_canonicalCaptionCount + 1));
    }

    #endregion

    #region GIF upload

    [Test]
    public async Task UploadGif_WithName_AppearsInTheLibraryAndIsStoredOnDisk()
    {
        var name = $"e2e synthetic gif {Guid.NewGuid():N}";
        var fixture = await WriteFixtureGifAsync();
        await Expect(_page.GifRows).ToHaveCountAsync(_canonicalGifCount);

        await UploadAndSubmitAsync(fixture, name);

        await Expect(_page.SnackbarWithText("GIF added successfully")).ToBeVisibleAsync();
        await Expect(_page.GifRow(name)).ToHaveCountAsync(1);
        await Expect(_page.GifRows).ToHaveCountAsync(_canonicalGifCount + 1);

        var stored = await ReadStoredGifAsync(name);
        Assert.That(stored.FilePath, Does.StartWith("ban-gifs/").And.EndWith(".gif"));
        Assert.That(stored.PhotoHash, Is.Not.Null.And.Length.EqualTo(8), "the hash is what later uploads are compared against");
        Assert.That(stored.FileId, Is.Null, "a fresh upload has no Telegram file_id cached yet");
        Assert.That(await File.ReadAllBytesAsync(FullMediaPath(stored.FilePath)), Is.EqualTo(FixtureGifBytes),
            "the uploaded bytes are stored as-is under this instance's media path");
        Assert.That(await CountStoredGifsAsync(), Is.EqualTo(_canonicalGifCount + 1));
    }

    [Test]
    public async Task UploadDuplicateGif_CancelUpload_LeavesTheLibraryUnchanged()
    {
        var firstName = $"e2e synthetic gif {Guid.NewGuid():N}";
        var secondName = $"e2e synthetic duplicate {Guid.NewGuid():N}";
        var fixture = await WriteFixtureGifAsync();
        await UploadAndSubmitAsync(fixture, firstName);
        await Expect(_page.GifRow(firstName)).ToHaveCountAsync(1);
        var first = await ReadStoredGifAsync(firstName);

        await _page.OpenAddGifDialogAsync();
        await _page.UploadFileAsync(fixture);
        await _page.WaitForFileSelectedAsync(Path.GetFileName(fixture));
        await _page.FillNameAsync(secondName);
        await _page.SubmitAsync();
        await _page.WaitForDuplicateWarningAsync();
        await Expect(_page.DuplicateWarning).ToContainTextAsync(firstName);
        await Expect(_page.SubmitButton).ToBeDisabledAsync();

        await _page.ClickCancelUploadAsync();

        await Expect(_page.Dialog).ToHaveCountAsync(0);
        await Expect(_page.GifRow(secondName)).ToHaveCountAsync(0);
        await Expect(_page.GifRows).ToHaveCountAsync(_canonicalGifCount + 1);

        await using var ctx = CreateDbContext();
        Assert.That(await ctx.BanCelebrationGifs.CountAsync(), Is.EqualTo(_canonicalGifCount + 1));
        Assert.That(await ctx.BanCelebrationGifs.AnyAsync(g => g.Name == secondName), Is.False,
            "the pending row is deleted when the upload is cancelled");
        var remainingFiles = Directory.GetFiles(Path.GetDirectoryName(FullMediaPath(first.FilePath))!, "*.gif");
        Assert.That(remainingFiles, Is.EqualTo(new[] { FullMediaPath(first.FilePath) }),
            "the cancelled upload's file is removed from disk; only the first upload remains");
    }

    [Test]
    public async Task UploadDuplicateGif_KeepBoth_AddsItAlongsideTheOriginal()
    {
        var firstName = $"e2e synthetic gif {Guid.NewGuid():N}";
        var secondName = $"e2e synthetic duplicate {Guid.NewGuid():N}";
        var fixture = await WriteFixtureGifAsync();
        await UploadAndSubmitAsync(fixture, firstName);
        await Expect(_page.GifRow(firstName)).ToHaveCountAsync(1);

        await _page.OpenAddGifDialogAsync();
        await _page.UploadFileAsync(fixture);
        await _page.WaitForFileSelectedAsync(Path.GetFileName(fixture));
        await _page.FillNameAsync(secondName);
        await _page.SubmitAsync();
        await _page.WaitForDuplicateWarningAsync();
        await Expect(_page.DuplicateWarning).ToContainTextAsync(firstName);

        await _page.ClickKeepBothAsync();

        await Expect(_page.GifRow(secondName)).ToHaveCountAsync(1);
        await Expect(_page.GifRow(firstName)).ToHaveCountAsync(1);
        await Expect(_page.GifRows).ToHaveCountAsync(_canonicalGifCount + 2);

        var first = await ReadStoredGifAsync(firstName);
        var second = await ReadStoredGifAsync(secondName);
        Assert.That(second.FilePath, Is.Not.EqualTo(first.FilePath), "each upload gets its own file");
        Assert.That(second.PhotoHash, Is.EqualTo(first.PhotoHash), "Keep Both stores the hash of the kept duplicate");
        Assert.That(File.Exists(FullMediaPath(second.FilePath)), Is.True);
        Assert.That(await CountStoredGifsAsync(), Is.EqualTo(_canonicalGifCount + 2));
    }

    #endregion

    #region GIF from URL

    [Test]
    public async Task AddGifFromUrl_DownloadsItFromTheUrlAndStoresIt()
    {
        var name = $"e2e synthetic url gif {Guid.NewGuid():N}";
        const string gifPath = "/celebration.gif";
        var gifHost = WireMockServer.Start();
        _gifHost = gifHost;
        gifHost.Given(Request.Create().WithPath(gifPath).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "image/gif")
                .WithBody(FixtureGifBytes));
        var url = $"{gifHost.Urls[0]}{gifPath}";

        await _page.OpenAddGifDialogAsync();
        await _page.SwitchToUrlTabAsync();
        await Expect(_page.SubmitButton).ToBeDisabledAsync();
        await _page.FillNameAsync(name);
        await _page.EnterUrlAsync(url);
        await Expect(_page.SubmitButton).ToBeEnabledAsync();
        await _page.SubmitAndWaitForCloseAsync();

        await Expect(_page.SnackbarWithText("GIF added successfully")).ToBeVisibleAsync();
        await Expect(_page.GifRow(name)).ToHaveCountAsync(1);
        await Expect(_page.GifRows).ToHaveCountAsync(_canonicalGifCount + 1);

        var stored = await ReadStoredGifAsync(name);
        Assert.That(stored.FilePath, Does.StartWith("ban-gifs/").And.EndWith(".gif"));
        Assert.That(stored.PhotoHash, Is.Not.Null.And.Length.EqualTo(8));
        Assert.That(await File.ReadAllBytesAsync(FullMediaPath(stored.FilePath)), Is.EqualTo(FixtureGifBytes),
            "the downloaded bytes are stored as-is under this instance's media path");
        Assert.That(await CountStoredGifsAsync(), Is.EqualTo(_canonicalGifCount + 1));

        var requests = gifHost.LogEntries.Select(e => e.RequestMessage?.Path).ToList();
        Assert.That(requests, Is.EqualTo(new[] { gifPath }), "the app fetched the URL exactly once");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// A 4x4 single-frame GIF89a of one solid colour, hand-encoded in the clear-code-before-every-pixel
    /// LZW form (3-bit codes, no table bookkeeping). Solid, so its average hash is all zero bits.
    /// </summary>
    private static readonly byte[] FixtureGifBytes = BuildSolidGif();

    private static byte[] BuildSolidGif()
    {
        const int W = 4, H = 4;
        var g = new List<byte>();

        g.AddRange("GIF89a"u8.ToArray());
        g.AddRange([W, 0, H, 0]);
        g.Add(0xF0);                        // global colour table, 2 entries
        g.AddRange([0, 0]);
        g.AddRange([0x80, 0x80, 0x80]);     // index 0: grey
        g.AddRange([0x80, 0x80, 0x80]);     // index 1: grey (unused)

        g.AddRange([0x2C, 0, 0, 0, 0, W, 0, H, 0, 0x00]);
        g.Add(0x02);                        // LZW minimum code size

        var codes = new List<int>();
        for (var i = 0; i < W * H; i++)
        {
            codes.Add(4);                   // clear
            codes.Add(0);                   // colour index 0
        }
        codes.Add(5);                       // end of information

        var bytes = new List<byte>();
        var accumulator = 0;
        var bitsHeld = 0;
        foreach (var code in codes)
        {
            accumulator |= code << bitsHeld;
            bitsHeld += 3;
            while (bitsHeld >= 8)
            {
                bytes.Add((byte)(accumulator & 0xFF));
                accumulator >>= 8;
                bitsHeld -= 8;
            }
        }
        if (bitsHeld > 0) bytes.Add((byte)(accumulator & 0xFF));

        g.Add((byte)bytes.Count);
        g.AddRange(bytes);
        g.Add(0x00);
        g.Add(0x3B);
        return g.ToArray();
    }

    /// <summary>Writes the fixture GIF to a uniquely named temp file (deleted in TearDown).</summary>
    private async Task<string> WriteFixtureGifAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"e2e-celebration-{Guid.NewGuid():N}.gif");
        await File.WriteAllBytesAsync(path, FixtureGifBytes);
        _fixtureFiles.Add(path);
        return path;
    }

    private async Task UploadAndSubmitAsync(string fixture, string name)
    {
        await _page.OpenAddGifDialogAsync();
        await _page.UploadFileAsync(fixture);
        await _page.WaitForFileSelectedAsync(Path.GetFileName(fixture));
        // Selecting a file auto-fills the name from the file name; overwrite it with the test's name.
        await _page.FillNameAsync(name);
        await Expect(_page.NameInput).ToHaveValueAsync(name);
        await _page.SubmitAndWaitForCloseAsync();
    }

    private async Task<StoredGif> ReadStoredGifAsync(string name)
    {
        await using var ctx = CreateDbContext();
        return await ctx.BanCelebrationGifs.AsNoTracking()
            .Where(g => g.Name == name)
            .Select(g => new StoredGif(g.FilePath, g.FileId, g.PhotoHash))
            .SingleAsync();
    }

    private async Task<int> CountStoredGifsAsync()
    {
        await using var ctx = CreateDbContext();
        return await ctx.BanCelebrationGifs.CountAsync();
    }

    /// <summary>Resolves a stored relative path the way the app does, under this instance's data path.</summary>
    private string FullMediaPath(string relativePath)
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>().GetFullPath(relativePath);
    }

    private sealed record StoredGif(string FilePath, string? FileId, byte[]? PhotoHash);

    #endregion
}
