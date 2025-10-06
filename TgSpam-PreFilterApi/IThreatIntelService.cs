namespace TgSpam_PreFilterApi;

public interface IThreatIntelService
{
    Task<bool> IsThreatAsync(string url, CancellationToken ct = default);
}