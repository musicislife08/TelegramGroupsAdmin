using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

/// <summary>
/// Mapping extensions between Data layer DTOs and Business layer models
/// for Welcome configuration.
/// </summary>
public static class WelcomeConfigMappings
{
    // ============================================================================
    // Root WelcomeConfig mappings
    // ============================================================================

    extension(WelcomeConfigData data)
    {
        public WelcomeConfig ToModel() => new()
        {
            Enabled = data.Enabled,
            Mode = (WelcomeMode)data.Mode,
            TimeoutSeconds = (int)data.TimeoutSeconds,
            JoinSecurity = (data.JoinSecurity ?? new JoinSecurityConfigData()).ToModel(),
            MainWelcomeMessage = data.MainWelcomeMessage,
            DmChatTeaserMessage = data.DmChatTeaserMessage,
            AcceptButtonText = data.AcceptButtonText,
            DenyButtonText = data.DenyButtonText,
            DmButtonText = data.DmButtonText,
            ExamConfig = data.ExamConfig?.ToModel()
        };
    }

    extension(WelcomeConfig model)
    {
        public WelcomeConfigData ToData() => new()
        {
            Enabled = model.Enabled,
            Mode = (int)model.Mode,
            TimeoutSeconds = model.TimeoutSeconds,
            JoinSecurity = model.JoinSecurity.ToData(),
            MainWelcomeMessage = model.MainWelcomeMessage,
            DmChatTeaserMessage = model.DmChatTeaserMessage,
            AcceptButtonText = model.AcceptButtonText,
            DenyButtonText = model.DenyButtonText,
            DmButtonText = model.DmButtonText,
            ExamConfig = model.ExamConfig?.ToData()
        };
    }

    // ============================================================================
    // JoinSecurityConfig mappings
    // ============================================================================

    extension(JoinSecurityConfigData data)
    {
        public JoinSecurityConfig ToModel() => new()
        {
            Cas = (data.Cas ?? new CasConfigData()).ToModel(),
            Impersonation = (data.Impersonation ?? new ImpersonationConfigData()).ToModel()
        };
    }

    extension(JoinSecurityConfig model)
    {
        public JoinSecurityConfigData ToData() => new()
        {
            Cas = model.Cas.ToData(),
            Impersonation = model.Impersonation.ToData()
        };
    }

    // ============================================================================
    // CasConfig mappings
    // ============================================================================

    extension(CasConfigData data)
    {
        public CasConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            ApiUrl = data.ApiUrl,
            Timeout = TimeSpan.FromSeconds(data.TimeoutSeconds),
            UserAgent = data.UserAgent,
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(CasConfig model)
    {
        public CasConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            ApiUrl = model.ApiUrl,
            TimeoutSeconds = model.Timeout.TotalSeconds,
            UserAgent = model.UserAgent,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // ImpersonationConfig mappings
    // ============================================================================

    extension(ImpersonationConfigData data)
    {
        public ImpersonationConfig ToModel() => new()
        {
            Enabled = data.Enabled
        };
    }

    extension(ImpersonationConfig model)
    {
        public ImpersonationConfigData ToData() => new()
        {
            Enabled = model.Enabled
        };
    }

    // ============================================================================
    // ExamConfig mappings
    // ============================================================================

    extension(ExamConfigData data)
    {
        public ExamConfig ToModel() => new()
        {
            McQuestions = data.McQuestions.Select(q => q.ToModel()).ToList(),
            McPassingThreshold = data.McPassingThreshold,
            OpenEndedQuestion = data.OpenEndedQuestion,
            GroupTopic = data.GroupTopic,
            EvaluationCriteria = data.EvaluationCriteria,
            RequireBothToPass = data.RequireBothToPass
        };
    }

    extension(ExamConfig model)
    {
        public ExamConfigData ToData() => new()
        {
            McQuestions = model.McQuestions.Select(q => q.ToData()).ToList(),
            McPassingThreshold = model.McPassingThreshold,
            OpenEndedQuestion = model.OpenEndedQuestion,
            GroupTopic = model.GroupTopic,
            EvaluationCriteria = model.EvaluationCriteria,
            RequireBothToPass = model.RequireBothToPass
        };
    }

    // ============================================================================
    // ExamMcQuestion mappings
    // ============================================================================

    extension(ExamMcQuestionData data)
    {
        public ExamMcQuestion ToModel() => new()
        {
            Question = data.Question,
            Answers = data.Answers
        };
    }

    extension(ExamMcQuestion model)
    {
        public ExamMcQuestionData ToData() => new()
        {
            Question = model.Question,
            Answers = model.Answers
        };
    }
}
