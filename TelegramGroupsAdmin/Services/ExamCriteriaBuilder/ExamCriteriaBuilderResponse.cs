namespace TelegramGroupsAdmin.Services.ExamCriteriaBuilder;

/// <summary>
/// Response model from criteria generation.
/// </summary>
public class ExamCriteriaBuilderResponse
{
    public bool Success { get; set; }
    public string? GeneratedCriteria { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Chat display name for snackbar notifications.
    /// </summary>
    public string? ChatDisplayName { get; set; }
}
