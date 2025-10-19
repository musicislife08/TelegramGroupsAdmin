namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// URL filtering enforcement mode
/// </summary>
public enum BlockMode
{
    /// <summary>URL filtering disabled for this source</summary>
    Disabled = 0,

    /// <summary>Soft block - contributes to spam confidence voting, subject to OpenAI veto</summary>
    Soft = 1,

    /// <summary>Hard block - instant ban before spam detection, no OpenAI veto</summary>
    Hard = 2
}
