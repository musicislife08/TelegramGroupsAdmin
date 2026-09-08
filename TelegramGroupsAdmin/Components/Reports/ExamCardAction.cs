using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Components.Reports;

/// <summary>An admin's chosen action on an exam review card.</summary>
public sealed record ExamCardAction(ExamResultRecord ExamResult, ExamAction Action);
