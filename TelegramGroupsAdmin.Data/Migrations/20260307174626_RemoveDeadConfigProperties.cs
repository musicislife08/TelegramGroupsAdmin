using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <summary>
    /// Strips dead V1 config properties from the JSONB config_json column.
    /// These properties were removed from the C# models because no check reads them:
    /// - ConfidenceThreshold on StopWords, Bayes, Spacing, Translation (checks hardcode or use BayesConstants)
    /// - SpaceRatioThreshold on Spacing (V1 algorithm concept, V2 uses short-word-ratio only)
    /// </summary>
    public partial class RemoveDeadConfigProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = config_json
                    #- '{StopWords,ConfidenceThreshold}'
                    #- '{Bayes,ConfidenceThreshold}'
                    #- '{Spacing,ConfidenceThreshold}'
                    #- '{Spacing,SpaceRatioThreshold}'
                    #- '{Translation,ConfidenceThreshold}'
                WHERE config_json IS NOT NULL;
                """);
        }

        /// <summary>
        /// Down is intentionally empty — removed JSONB properties cannot be restored
        /// because the original per-row values are unknown.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
