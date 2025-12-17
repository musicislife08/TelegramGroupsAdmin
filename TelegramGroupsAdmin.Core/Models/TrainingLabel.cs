namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Training label for ML spam classification.
/// Stored as smallint in database: 0=Spam, 1=Ham.
/// Repositories handle conversion between short and enum.
/// </summary>
public enum TrainingLabel : short
{
    /// <summary>
    /// Message is spam (malicious, unwanted) - stored as 0
    /// </summary>
    Spam = 0,

    /// <summary>
    /// Message is ham (legitimate, wanted) - stored as 1
    /// </summary>
    Ham = 1
}
