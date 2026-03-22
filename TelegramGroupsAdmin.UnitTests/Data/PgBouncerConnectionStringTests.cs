using Npgsql;
using static TelegramGroupsAdmin.Data.Extensions.ServiceCollectionExtensions;

namespace TelegramGroupsAdmin.UnitTests.Data;

[TestFixture]
public class PgBouncerConnectionStringTests
{
    [Test]
    public void ApplyPgBouncerSettings_WhenCalled_SetsNoResetOnClose()
    {
        var connectionString = "Host=localhost;Database=testdb;Username=user;Password=pass";

        var result = ApplyPgBouncerSettings(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(result);
        Assert.That(builder.NoResetOnClose, Is.True);
    }

    [Test]
    public void ApplyPgBouncerSettings_PreservesExistingSettings()
    {
        var connectionString = "Host=myhost;Port=6432;Database=testdb;Username=user;Password=pass;Timeout=30";

        var result = ApplyPgBouncerSettings(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(result);
        Assert.That(builder.Host, Is.EqualTo("myhost"));
        Assert.That(builder.Port, Is.EqualTo(6432));
        Assert.That(builder.Database, Is.EqualTo("testdb"));
        Assert.That(builder.Timeout, Is.EqualTo(30));
        Assert.That(builder.NoResetOnClose, Is.True);
    }

    [Test]
    public void ApplyPgBouncerSettings_OverridesExplicitFalse()
    {
        var connectionString = "Host=localhost;Database=testdb;No Reset On Close=false";

        var result = ApplyPgBouncerSettings(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(result);
        Assert.That(builder.NoResetOnClose, Is.True);
    }
}
