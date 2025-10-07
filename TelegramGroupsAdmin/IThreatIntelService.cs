namespace TelegramGroupsAdmin;

public interface IThreatIntelService
{
    Task<bool> IsThreatAsync(string url, CancellationToken ct = default);
}