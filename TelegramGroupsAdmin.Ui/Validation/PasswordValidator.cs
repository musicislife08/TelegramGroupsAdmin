namespace TelegramGroupsAdmin.Ui.Validation;

/// <summary>
/// Shared password validation logic for Register and ResetPassword pages.
/// Ensures consistent password requirements across the application.
/// </summary>
public static class PasswordValidator
{
    /// <summary>
    /// Minimum password length requirement.
    /// </summary>
    public const int MinLength = 8;

    /// <summary>
    /// Helper text describing password requirements for UI display.
    /// </summary>
    public const string RequirementsHelperText = "Minimum 8 characters with uppercase, lowercase, and number";

    /// <summary>
    /// Validates a password meets strength requirements.
    /// </summary>
    /// <param name="password">The password to validate.</param>
    /// <returns>Error message if invalid, null if valid.</returns>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "Password is required";

        if (password.Length < MinLength)
            return $"Password must be at least {MinLength} characters";

        if (!password.Any(char.IsUpper))
            return "Password must contain at least one uppercase letter";

        if (!password.Any(char.IsLower))
            return "Password must contain at least one lowercase letter";

        if (!password.Any(char.IsDigit))
            return "Password must contain at least one number";

        return null;
    }

    /// <summary>
    /// Validates that a confirmation password matches the original.
    /// </summary>
    /// <param name="confirmPassword">The confirmation password.</param>
    /// <param name="originalPassword">The original password to match against.</param>
    /// <returns>Error message if invalid, null if valid.</returns>
    public static string? ValidateConfirmation(string? confirmPassword, string? originalPassword)
    {
        if (string.IsNullOrWhiteSpace(confirmPassword))
            return "Please confirm your password";

        if (confirmPassword != originalPassword)
            return "Passwords do not match";

        return null;
    }
}
