namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Type of protected entity that may be impersonated
/// </summary>
public enum ProtectedEntityType
{
    /// <summary>Admin user (Telegram user who is admin in a managed chat)</summary>
    User,
    /// <summary>Managed chat group name</summary>
    Chat,
    /// <summary>Linked channel name/photo</summary>
    Channel
}
