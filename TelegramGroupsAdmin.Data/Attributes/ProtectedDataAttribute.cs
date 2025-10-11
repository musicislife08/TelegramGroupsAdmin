namespace TelegramGroupsAdmin.Data.Attributes;

/// <summary>
/// Indicates that this property contains Data Protection encrypted data
/// that must be decrypted during export and re-encrypted during import
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ProtectedDataAttribute : Attribute
{
}
