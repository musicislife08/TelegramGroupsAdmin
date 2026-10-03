namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Three-way answer to "is the email service configured?" for decisions that must fail closed
/// (e.g. whether a new account may start email-verified). The boolean gates on
/// <see cref="IFeatureAvailabilityService"/> collapse <see cref="Indeterminate"/> to false, which is the
/// right call for UI toggles but would silently waive verification on a config-read failure.
/// </summary>
public enum EmailConfigurationState
{
    /// <summary>SendGrid is enabled with a from-address and an API key: verification emails can be sent.</summary>
    Enabled,

    /// <summary>Genuinely not configured (disabled, no from-address, or no SendGrid key stored).</summary>
    Disabled,

    /// <summary>Could not be determined: the config read threw, or stored API keys failed to decrypt.</summary>
    Indeterminate
}
