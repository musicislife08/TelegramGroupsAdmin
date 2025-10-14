using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Add confidence aggregation and training flags to detection_results table.
///
/// Phase 2.6: Confidence Aggregation & Training System
/// - used_for_training: Flag to mark high-quality training samples (admin decisions, confident AI)
/// - net_confidence: Weighted voting result = Sum(spam confidences) - Sum(ham confidences)
///
/// Benefits:
/// - Reduces false positives through weighted voting
/// - Improves training data quality (only use confident samples)
/// - Enables two-tier decision system (OpenAI veto vs admin review)
/// </summary>
[Migration(202601106)]
public class AddConfidenceFieldsToDetectionResults : Migration
{
    public override void Up()
    {
        // Add used_for_training flag (default true for backwards compatibility)
        // All existing detection_results are considered training-worthy
        Alter.Table("detection_results")
            .AddColumn("used_for_training").AsBoolean().NotNullable().WithDefaultValue(true);

        // Add net_confidence for weighted voting result
        // NULL for legacy records (before Phase 2.6), INT for new records
        Alter.Table("detection_results")
            .AddColumn("net_confidence").AsInt32().Nullable();

        // Add index for training data queries (used by Bayes and Similarity checks)
        Create.Index("idx_detection_results_training")
            .OnTable("detection_results")
            .OnColumn("used_for_training")
            .Ascending()
            .OnColumn("is_spam")
            .Ascending();
    }

    public override void Down()
    {
        Delete.Index("idx_detection_results_training").OnTable("detection_results");
        Delete.Column("net_confidence").FromTable("detection_results");
        Delete.Column("used_for_training").FromTable("detection_results");
    }
}
