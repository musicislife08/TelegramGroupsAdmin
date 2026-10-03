using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Http;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// <see cref="IBanCelebrationGifRepository.GetByIdAsync"/> against canonical
/// <c>07_ban_celebration_gifs.sql</c> (92 reference rows). The anchor row is read at runtime.
/// </summary>
[TestFixture]
public class BanCelebrationGifRepositoryGetByIdTests
{
    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;
    private string _tempMediaPath = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();
        _tempMediaPath = Path.Combine(Path.GetTempPath(), $"GifGetById_{Guid.NewGuid():N}");

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddPublicUrlFetcher();
        services.AddSingleton(Substitute.For<IVideoFrameExtractionService>());
        services.AddSingleton(Options.Create(new AppOptions { DataPath = _tempMediaPath }));
        services.AddScoped<IBanCelebrationGifRepository, BanCelebrationGifRepository>();
        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
        _testHelper?.Dispose();
        if (Directory.Exists(_tempMediaPath))
        {
            Directory.Delete(_tempMediaPath, recursive: true);
        }
    }

    [Test]
    public async Task GetByIdAsync_ExistingCanonicalGif_ReturnsIt()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var anchor = await ctx.BanCelebrationGifs.AsNoTracking().OrderBy(g => g.Id).FirstAsync();
        using var scope = _serviceProvider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();

        var gif = await repo.GetByIdAsync(anchor.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(gif, Is.Not.Null);
            Assert.That(gif!.FilePath, Is.EqualTo(anchor.FilePath));
            Assert.That(gif.FileId, Is.EqualTo(anchor.FileId));
        }
    }

    [Test]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var unknownId = await ctx.BanCelebrationGifs.MaxAsync(g => g.Id) + 1;
        using var scope = _serviceProvider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();

        Assert.That(await repo.GetByIdAsync(unknownId), Is.Null);
    }
}
