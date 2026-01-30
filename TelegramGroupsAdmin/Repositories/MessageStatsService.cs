using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Models.Analytics;
using TelegramGroupsAdmin.Repositories.Mappings;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Service for message analytics and statistics
/// Extracted from MessageHistoryRepository (REFACTOR-3)
/// </summary>
public class MessageStatsService : IMessageStatsService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MessageStatsService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<HistoryStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var totalMessages = await context.Messages.CountAsync(cancellationToken);
        var uniqueUsers = await context.Messages.Select(m => m.UserId).Distinct().CountAsync(cancellationToken);
        var photoCount = await context.Messages.CountAsync(m => m.PhotoFileId != null, cancellationToken);

        DateTimeOffset? oldestTimestamp = null;
        DateTimeOffset? newestTimestamp = null;

        if (totalMessages > 0)
        {
            oldestTimestamp = await context.Messages.MinAsync(m => m.Timestamp, cancellationToken);
            newestTimestamp = await context.Messages.MaxAsync(m => m.Timestamp, cancellationToken);
        }

        var result = new HistoryStats(
            TotalMessages: totalMessages,
            UniqueUsers: uniqueUsers,
            PhotoCount: photoCount,
            OldestTimestamp: oldestTimestamp,
            NewestTimestamp: newestTimestamp);

        return result;
    }

    public async Task<SpamSummaryStats> GetDetectionStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Overall stats from detection_results
        var allDetections = await context.DetectionResults
            .AsNoTracking()
            .Select(dr => new { dr.IsSpam, dr.Confidence })
            .ToListAsync(cancellationToken);

        var total = allDetections.Count;
        var spam = allDetections.Count(d => d.IsSpam);
        var avgConfidence = allDetections.Any()
            ? allDetections.Average(d => (double)d.Confidence)
            : 0.0;

        // Last 24h stats
        var since24h = DateTimeOffset.UtcNow.AddDays(AnalyticsConstants.Last24HoursLookbackDays);
        var recentDetections = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.DetectedAt >= since24h)
            .Select(dr => dr.IsSpam)
            .ToListAsync(cancellationToken);

        var recentTotal = recentDetections.Count;
        var recentSpam = recentDetections.Count(s => s);

        return new SpamSummaryStats
        {
            TotalDetections = total,
            SpamDetected = spam,
            SpamPercentage = total > 0 ? (double)spam / total * 100 : 0,
            AverageConfidence = avgConfidence,
            Last24hDetections = recentTotal,
            Last24hSpam = recentSpam,
            Last24hSpamPercentage = recentTotal > 0 ? (double)recentSpam / recentTotal * 100 : 0
        };
    }

    public async Task<List<RecentDetection>> GetRecentDetectionsAsync(int limit = AnalyticsConstants.DefaultRecentDetectionsLimit, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Use EnrichedDetectionView to eliminate complex 4-table JOIN chain
        // View pre-joins: detection_results → messages → users → telegram_users
        var results = await context.EnrichedDetections
            .AsNoTracking()
            .OrderByDescending(v => v.DetectedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results.Select(v => v.ToModel()).ToList();
    }

    public async Task<MessageTrendsData> GetMessageTrendsAsync(
        List<long> chatIds,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // === CONSOLIDATED QUERY 1: Fetch all message data with spam flag in single query ===
        // Uses LEFT JOIN to include all messages, marking spam where detection results exist
        var allMessageData = await (
            from m in context.Messages
            where m.Timestamp >= startDate && m.Timestamp <= endDate
            where chatIds.Count == 0 || chatIds.Contains(m.ChatId)
            join dr in context.DetectionResults on m.MessageId equals dr.MessageId into detections
            from dr in detections.DefaultIfEmpty()
            select new
            {
                m.MessageId,
                m.UserId,
                m.Timestamp,
                m.ChatId,
                m.ContentCheckSkipReason,
                IsSpam = dr != null && dr.IsSpam
            }
        ).AsNoTracking().ToListAsync(cancellationToken);

        // === DE-DUPLICATE MESSAGES ===
        // LEFT JOIN can produce duplicate rows if a message has multiple detection results
        // De-duplicate by MessageId to ensure accurate counts
        var distinctMessages = allMessageData
            .GroupBy(m => m.MessageId)
            .Select(g => g.First()) // Take first occurrence of each message
            .ToList();

        // === CONSOLIDATED QUERY 2: Per-chat breakdown with managed chat names ===
        var perChatVolume = await (
            from m in context.Messages
            where m.Timestamp >= startDate && m.Timestamp <= endDate
            where chatIds.Count == 0 || chatIds.Contains(m.ChatId)
            join c in context.ManagedChats on m.ChatId equals c.ChatId
            group m by new { c.ChatId, c.ChatName } into g
            select new ChatVolumeData
            {
                ChatName = g.Key.ChatName ?? g.Key.ChatId.ToString(),
                Count = g.Count()
            })
            .OrderByDescending(c => c.Count)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // === ALL REMAINING CALCULATIONS DONE IN-MEMORY ===
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var daysDiff = (endDate - startDate).TotalDays;

        // Basic metrics (use distinctMessages to avoid duplication)
        var totalMessages = distinctMessages.Count;
        var uniqueUsers = distinctMessages.Select(m => m.UserId).Distinct().Count();
        var dailyAverage = daysDiff > 0 ? totalMessages / daysDiff : 0;

        // Spam metrics
        var spamMessages = distinctMessages.Where(m => m.IsSpam).ToList();
        var spamCount = spamMessages.Count; // Already distinct after de-duplication
        var spamPercentage = totalMessages > 0 ? (spamCount / (double)totalMessages * 100.0) : 0;

        // Daily volume - all messages grouped by local date
        var dailyVolume = distinctMessages
            .GroupBy(m =>
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .Select(g => new DailyVolumeData
            {
                Date = g.Key,
                Count = g.Count()
            })
            .OrderBy(d => d.Date)
            .ToList();

        // Daily spam - spam messages grouped by local date
        var dailySpam = spamMessages
            .GroupBy(m =>
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .Select(g => new DailyVolumeData
            {
                Date = g.Key,
                Count = g.Count() // Already distinct after de-duplication
            })
            .OrderBy(d => d.Date)
            .ToList();

        // Daily ham - non-spam messages grouped by local date
        var hamMessages = distinctMessages.Where(m => !m.IsSpam).ToList();
        var dailyHam = hamMessages
            .GroupBy(m =>
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .Select(g => new DailyVolumeData
            {
                Date = g.Key,
                Count = g.Count()
            })
            .OrderBy(d => d.Date)
            .ToList();

        // === NEW AGGREGATIONS (UX-2.1) ===

        // 1. Peak Activity Summary (all messages, hourly + daily)
        PeakActivitySummary? peakActivity = null;
        if (totalMessages > 0)
        {
            // Hourly activity (always available)
            var hourlyActivity = distinctMessages
                .GroupBy(m =>
                {
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                    return localTime.Hour;
                })
                .Select(g => (hour: g.Key, count: g.Count()))
                .ToList();

            var peakHourRange = DetectHourlyRange(hourlyActivity);

            // Daily activity (only if >= 7 days of data)
            string? peakDayRange = null;
            var hasEnoughDataForWeekly = daysDiff >= AnalyticsConstants.MinDaysForWeeklyPattern;
            if (hasEnoughDataForWeekly)
            {
                var dailyActivity = distinctMessages
                    .GroupBy(m =>
                    {
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                        return localTime.DayOfWeek;
                    })
                    .Select(g => (day: g.Key, count: g.Count()))
                    .ToList();

                peakDayRange = DetectDayRange(dailyActivity);
            }

            peakActivity = new PeakActivitySummary
            {
                PeakHourRange = peakHourRange,
                PeakDayRange = peakDayRange,
                HasEnoughDataForWeekly = hasEnoughDataForWeekly
            };
        }

        // 2. Spam Seasonality Summary (spam only, hourly + daily + monthly)
        ContentSeasonalitySummary? spamSeasonality = null;
        if (spamMessages.Count > 0)
        {
            // Hourly spam pattern (always available)
            var hourlySpam = spamMessages
                .GroupBy(m =>
                {
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                    return localTime.Hour;
                })
                .Select(g => (hour: g.Key, count: g.Count()))
                .ToList();

            var hourlyPattern = DetectHourlyRange(hourlySpam);

            // Weekly spam pattern (only if >= 7 days)
            string? weeklyPattern = null;
            var hasWeeklyData = daysDiff >= AnalyticsConstants.MinDaysForWeeklyPattern;
            if (hasWeeklyData)
            {
                var dailySpamPattern = spamMessages
                    .GroupBy(m =>
                    {
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                        return localTime.DayOfWeek;
                    })
                    .Select(g => (day: g.Key, count: g.Count()))
                    .ToList();

                weeklyPattern = DetectDayRange(dailySpamPattern);
            }

            // Monthly spam pattern (only if >= 60 days and spans multiple months)
            string? monthlyPattern = null;
            var spanMonths = startDate.Month != endDate.Month || startDate.Year != endDate.Year;
            var hasMonthlyData = daysDiff >= AnalyticsConstants.MinDaysForMonthlyPattern && spanMonths;
            if (hasMonthlyData)
            {
                var monthlySpamPattern = spamMessages
                    .GroupBy(m =>
                    {
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                        return localTime.Month;
                    })
                    .Select(g => (month: g.Key, count: g.Count()))
                    .ToList();

                monthlyPattern = DetectMonthRange(monthlySpamPattern);
            }

            spamSeasonality = new ContentSeasonalitySummary
            {
                HourlyPattern = hourlyPattern,
                WeeklyPattern = weeklyPattern,
                MonthlyPattern = monthlyPattern,
                HasWeeklyData = hasWeeklyData,
                HasMonthlyData = hasMonthlyData
            };
        }

        // 3. Week-over-Week Growth (only if >= 14 days) - all in-memory calculations
        WeekOverWeekGrowth? weekOverWeekGrowth = null;
        var hasPreviousPeriod = daysDiff >= AnalyticsConstants.MinDaysForWeekOverWeekGrowth;
        if (hasPreviousPeriod)
        {
            // Split date range into current week (last 7 days) and previous week (7-14 days ago)
            var currentWeekStart = endDate.AddDays(AnalyticsConstants.CurrentWeekLookbackDays);
            var previousWeekStart = endDate.AddDays(AnalyticsConstants.PreviousWeekLookbackDays);
            var previousWeekEnd = currentWeekStart;

            // Current week metrics - in-memory filtering
            var currentWeekData = distinctMessages.Where(m => m.Timestamp >= currentWeekStart).ToList();
            var currentWeekMessages = currentWeekData.Count;
            var currentWeekUsers = currentWeekData.Select(m => m.UserId).Distinct().Count();
            var currentWeekSpam = currentWeekData.Where(m => m.IsSpam).Count();
            var currentWeekSpamPct = currentWeekMessages > 0
                ? (currentWeekSpam / (double)currentWeekMessages * 100.0)
                : 0;

            // Previous week metrics - in-memory filtering
            var previousWeekData = distinctMessages
                .Where(m => m.Timestamp >= previousWeekStart && m.Timestamp < previousWeekEnd)
                .ToList();
            var previousWeekMessages = previousWeekData.Count;
            var previousWeekUsers = previousWeekData.Select(m => m.UserId).Distinct().Count();
            var previousWeekSpam = previousWeekData.Where(m => m.IsSpam).Count();
            var previousWeekSpamPct = previousWeekMessages > 0
                ? (previousWeekSpam / (double)previousWeekMessages * 100.0)
                : 0;

            // Calculate percentage growth
            var messageGrowth = previousWeekMessages > 0
                ? ((currentWeekMessages - previousWeekMessages) / (double)previousWeekMessages * 100.0)
                : 0;
            var userGrowth = previousWeekUsers > 0
                ? ((currentWeekUsers - previousWeekUsers) / (double)previousWeekUsers * 100.0)
                : 0;
            var spamGrowth = previousWeekSpamPct > 0
                ? ((currentWeekSpamPct - previousWeekSpamPct) / previousWeekSpamPct * 100.0)
                : 0;

            weekOverWeekGrowth = new WeekOverWeekGrowth
            {
                MessageGrowthPercent = messageGrowth,
                UserGrowthPercent = userGrowth,
                SpamGrowthPercent = spamGrowth,
                HasPreviousPeriod = true
            };
        }

        // 4. Top Active Users (top 5)
        var topActiveUsers = await GetTopActiveUsersAsync(
            context,
            limit: AnalyticsConstants.TopActiveUsersLimit,
            startDate: startDate,
            endDate: endDate,
            chatIds: chatIds.Count > 0 ? chatIds : null,
            cancellationToken: cancellationToken);

        // 5. Trusted User Breakdown (by content_check_skip_reason) - in-memory grouping
        TrustedUserBreakdown? trustedBreakdown = null;
        if (totalMessages > 0)
        {
            var breakdownData = distinctMessages
                .GroupBy(m => m.ContentCheckSkipReason)
                .Select(g => new { Reason = g.Key, Count = g.Count() })
                .ToList();

            var trustedCount = breakdownData
                .Where(x => x.Reason == Data.Models.ContentCheckSkipReason.UserTrusted)
                .Sum(x => x.Count);
            var adminCount = breakdownData
                .Where(x => x.Reason == Data.Models.ContentCheckSkipReason.UserAdmin)
                .Sum(x => x.Count);
            var untrustedCount = breakdownData
                .Where(x => x.Reason == Data.Models.ContentCheckSkipReason.NotSkipped)
                .Sum(x => x.Count);

            trustedBreakdown = new TrustedUserBreakdown
            {
                TrustedMessages = trustedCount,
                UntrustedMessages = untrustedCount,
                AdminMessages = adminCount,
                TrustedPercentage = (trustedCount / (double)totalMessages * 100.0),
                UntrustedPercentage = (untrustedCount / (double)totalMessages * 100.0),
                AdminPercentage = (adminCount / (double)totalMessages * 100.0)
            };
        }

        // 6. Daily Active Users (unique users per day) - in-memory grouping
        var dailyActiveUsers = distinctMessages
            .GroupBy(m =>
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Timestamp.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .Select(g => new DailyActiveUsersData
            {
                Date = g.Key,
                UniqueUsers = g.Select(x => x.UserId).Distinct().Count()
            })
            .OrderBy(d => d.Date)
            .ToList();

        return new MessageTrendsData
        {
            // Existing metrics (UX-2)
            TotalMessages = totalMessages,
            DailyAverage = dailyAverage,
            UniqueUsers = uniqueUsers,
            SpamPercentage = spamPercentage,
            DailyVolume = dailyVolume,
            DailySpam = dailySpam,
            DailyHam = dailyHam,
            PerChatVolume = perChatVolume,

            // New metrics (UX-2.1)
            PeakActivity = peakActivity,
            SpamSeasonality = spamSeasonality,
            WeekOverWeekGrowth = weekOverWeekGrowth,
            TopActiveUsers = topActiveUsers,
            TrustedUserBreakdown = trustedBreakdown,
            DailyActiveUsers = dailyActiveUsers
        };
    }

    #region Smart Range Detection Helpers (UX-2.1)

    /// <summary>
    /// Detect hourly activity ranges from message counts
    /// Returns formatted string like "7am-11am, 5pm-8pm" or "3am, 7pm"
    /// </summary>
    private static string DetectHourlyRange(List<(int hour, int count)> hourlyData, int topN = AnalyticsConstants.TopPeakHoursCount)
    {
        if (hourlyData.Count == 0)
            return "No data";

        // Take top N hours by count
        var topHours = hourlyData
            .OrderByDescending(x => x.count)
            .Take(topN)
            .Select(x => x.hour)
            .OrderBy(h => h)
            .ToList();

        if (topHours.Count == 0)
            return "No data";

        // Find consecutive ranges
        var ranges = new List<string>();
        var rangeStart = topHours[0];
        var rangeEnd = topHours[0];

        for (int i = 1; i < topHours.Count; i++)
        {
            if (topHours[i] == rangeEnd + 1)
            {
                // Consecutive hour, extend range
                rangeEnd = topHours[i];
            }
            else
            {
                // Gap found, save current range and start new one
                ranges.Add(FormatHourRange(rangeStart, rangeEnd));
                rangeStart = topHours[i];
                rangeEnd = topHours[i];
            }
        }

        // Add final range
        ranges.Add(FormatHourRange(rangeStart, rangeEnd));

        return string.Join(", ", ranges);
    }

    /// <summary>
    /// Format hour or hour range for display
    /// </summary>
    private static string FormatHourRange(int start, int end)
    {
        if (start == end)
        {
            // Single hour
            return FormatHour(start);
        }
        else
        {
            // Range
            return $"{FormatHour(start)}-{FormatHour(end)}";
        }
    }

    /// <summary>
    /// Format hour as 12-hour time (e.g., "3am", "11pm")
    /// </summary>
    private static string FormatHour(int hour)
    {
        if (hour == AnalyticsConstants.MidnightHour) return "12am";
        if (hour < AnalyticsConstants.NoonHour) return $"{hour}am";
        if (hour == AnalyticsConstants.NoonHour) return "12pm";
        return $"{hour - AnalyticsConstants.TwelveHourOffset}pm";
    }

    /// <summary>
    /// Detect day-of-week activity ranges
    /// Returns formatted string like "Mon-Wed" or "Saturday"
    /// </summary>
    private static string DetectDayRange(List<(DayOfWeek day, int count)> dailyData, int topN = AnalyticsConstants.TopPeakDaysCount)
    {
        if (dailyData.Count == 0)
            return "No data";

        // Take top N days by count
        var topDays = dailyData
            .OrderByDescending(x => x.count)
            .Take(topN)
            .Select(x => (int)x.day) // Convert to int for consecutive checking
            .OrderBy(d => d)
            .ToList();

        if (topDays.Count == 0)
            return "No data";

        // Find consecutive ranges
        var ranges = new List<string>();
        var rangeStart = topDays[0];
        var rangeEnd = topDays[0];

        for (int i = 1; i < topDays.Count; i++)
        {
            if (topDays[i] == rangeEnd + 1)
            {
                // Consecutive day, extend range
                rangeEnd = topDays[i];
            }
            else
            {
                // Gap found, save current range and start new one
                ranges.Add(FormatDayRange(rangeStart, rangeEnd));
                rangeStart = topDays[i];
                rangeEnd = topDays[i];
            }
        }

        // Add final range
        ranges.Add(FormatDayRange(rangeStart, rangeEnd));

        return string.Join(", ", ranges);
    }

    /// <summary>
    /// Format day or day range for display
    /// </summary>
    private static string FormatDayRange(int start, int end)
    {
        if (start == end)
        {
            // Single day
            return ((DayOfWeek)start).ToString();
        }
        else if (end - start == AnalyticsConstants.MinConsecutiveDaysForRange)
        {
            // Two consecutive days - show both
            return $"{((DayOfWeek)start).ToString()[..AnalyticsConstants.DayNameAbbreviationLength]}-{((DayOfWeek)end).ToString()[..AnalyticsConstants.DayNameAbbreviationLength]}";
        }
        else
        {
            // Range of 3+ days
            return $"{((DayOfWeek)start).ToString()[..AnalyticsConstants.DayNameAbbreviationLength]}-{((DayOfWeek)end).ToString()[..AnalyticsConstants.DayNameAbbreviationLength]}";
        }
    }

    /// <summary>
    /// Detect monthly activity ranges
    /// Returns formatted string like "November" or "Nov-Dec"
    /// </summary>
    private static string DetectMonthRange(List<(int month, int count)> monthlyData, int topN = AnalyticsConstants.TopPeakMonthsCount)
    {
        if (monthlyData.Count == 0)
            return "No data";

        // Take top N months by count
        var topMonths = monthlyData
            .OrderByDescending(x => x.count)
            .Take(topN)
            .Select(x => x.month)
            .OrderBy(m => m)
            .ToList();

        if (topMonths.Count == 0)
            return "No data";

        // Find consecutive ranges
        var ranges = new List<string>();
        var rangeStart = topMonths[0];
        var rangeEnd = topMonths[0];

        for (int i = 1; i < topMonths.Count; i++)
        {
            if (topMonths[i] == rangeEnd + 1)
            {
                // Consecutive month, extend range
                rangeEnd = topMonths[i];
            }
            else
            {
                // Gap found, save current range and start new one
                ranges.Add(FormatMonthRange(rangeStart, rangeEnd));
                rangeStart = topMonths[i];
                rangeEnd = topMonths[i];
            }
        }

        // Add final range
        ranges.Add(FormatMonthRange(rangeStart, rangeEnd));

        return string.Join(", ", ranges);
    }

    /// <summary>
    /// Format month or month range for display
    /// </summary>
    private static string FormatMonthRange(int start, int end)
    {
        var monthNames = new[] { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        if (start == end)
        {
            // Single month - show full name
            return new DateTime(AnalyticsConstants.MonthNameFormattingYear, start, AnalyticsConstants.MonthNameFormattingDay).ToString("MMMM");
        }
        else
        {
            // Range - show abbreviated
            return $"{monthNames[start]}-{monthNames[end]}";
        }
    }

    #endregion

    #region Top Active Users

    /// <summary>
    /// Get top active users for the specified period.
    /// Analytics-specific implementation (moved from TelegramUserRepository).
    /// </summary>
    private static async Task<List<TopActiveUser>> GetTopActiveUsersAsync(
        AppDbContext context,
        int limit,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        List<long>? chatIds,
        CancellationToken cancellationToken)
    {
        // Build base query with date filter
        var query = from u in context.TelegramUsers
                    join m in context.Messages on u.TelegramUserId equals m.UserId
                    where m.Timestamp >= startDate
                        && m.Timestamp <= endDate
                        && !u.IsBot
                    select new { u, m };

        // Apply optional chat filter
        if (chatIds != null && chatIds.Count > 0)
        {
            query = query.Where(x => chatIds.Contains(x.m.ChatId));
        }

        // Get total message count for percentage calculation
        var totalMessages = await query.CountAsync(cancellationToken);

        // Get top users
        var topUsers = await query
            .GroupBy(x => new { x.u.TelegramUserId, x.u.Username, x.u.FirstName, x.u.LastName, x.u.UserPhotoPath })
            .Select(g => new TopActiveUser
            {
                TelegramUserId = g.Key.TelegramUserId,
                Username = g.Key.Username,
                FirstName = g.Key.FirstName,
                LastName = g.Key.LastName,
                UserPhotoPath = g.Key.UserPhotoPath,
                MessageCount = g.Count(),
                Percentage = totalMessages > 0 ? (g.Count() / (double)totalMessages * 100.0) : 0
            })
            .OrderByDescending(u => u.MessageCount)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return topUsers;
    }

    #endregion
}
