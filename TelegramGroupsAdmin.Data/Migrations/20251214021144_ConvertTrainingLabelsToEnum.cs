using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertTrainingLabelsToEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Drop the old check constraint (requires string values)
            migrationBuilder.DropCheckConstraint(
                name: "CK_training_labels_label",
                table: "training_labels");

            // Step 2: Drop indexes that reference the label column
            migrationBuilder.DropIndex(
                name: "IX_training_labels_label",
                table: "training_labels");

            migrationBuilder.DropIndex(
                name: "IX_training_labels_label_labeled_at",
                table: "training_labels");

            // Step 3: Add temporary column with new type
            migrationBuilder.AddColumn<short>(
                name: "label_temp",
                table: "training_labels",
                type: "smallint",
                nullable: true);

            // Step 4: Migrate data ('spam' → 0, 'ham' → 1)
            migrationBuilder.Sql(@"
                UPDATE training_labels
                SET label_temp = CASE
                    WHEN label = 'spam' THEN 0
                    WHEN label = 'ham' THEN 1
                    ELSE NULL
                END;
            ");

            // Step 5: Drop old column
            migrationBuilder.DropColumn(
                name: "label",
                table: "training_labels");

            // Step 6: Rename temp column to label
            migrationBuilder.RenameColumn(
                name: "label_temp",
                table: "training_labels",
                newName: "label");

            // Step 7: Make label NOT NULL
            migrationBuilder.AlterColumn<short>(
                name: "label",
                table: "training_labels",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldNullable: true);

            // Step 8: Add new check constraint (numeric values: 0=Spam, 1=Ham)
            migrationBuilder.AddCheckConstraint(
                name: "CK_training_labels_label",
                table: "training_labels",
                sql: "label IN (0, 1)");

            // Step 9: Recreate indexes
            migrationBuilder.CreateIndex(
                name: "IX_training_labels_label",
                table: "training_labels",
                column: "label");

            migrationBuilder.CreateIndex(
                name: "IX_training_labels_label_labeled_at",
                table: "training_labels",
                columns: new[] { "label", "labeled_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Step 1: Drop numeric check constraint
            migrationBuilder.DropCheckConstraint(
                name: "CK_training_labels_label",
                table: "training_labels");

            // Step 2: Drop indexes
            migrationBuilder.DropIndex(
                name: "IX_training_labels_label",
                table: "training_labels");

            migrationBuilder.DropIndex(
                name: "IX_training_labels_label_labeled_at",
                table: "training_labels");

            // Step 3: Add temporary string column
            migrationBuilder.AddColumn<string>(
                name: "label_temp",
                table: "training_labels",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            // Step 4: Migrate data back (0 → 'spam', 1 → 'ham')
            migrationBuilder.Sql(@"
                UPDATE training_labels
                SET label_temp = CASE
                    WHEN label = 0 THEN 'spam'
                    WHEN label = 1 THEN 'ham'
                    ELSE NULL
                END;
            ");

            // Step 5: Drop numeric column
            migrationBuilder.DropColumn(
                name: "label",
                table: "training_labels");

            // Step 6: Rename temp column to label
            migrationBuilder.RenameColumn(
                name: "label_temp",
                table: "training_labels",
                newName: "label");

            // Step 7: Make label NOT NULL
            migrationBuilder.AlterColumn<string>(
                name: "label",
                table: "training_labels",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            // Step 8: Add string check constraint
            migrationBuilder.AddCheckConstraint(
                name: "CK_training_labels_label",
                table: "training_labels",
                sql: "label IN ('spam', 'ham')");

            // Step 9: Recreate indexes
            migrationBuilder.CreateIndex(
                name: "IX_training_labels_label",
                table: "training_labels",
                column: "label");

            migrationBuilder.CreateIndex(
                name: "IX_training_labels_label_labeled_at",
                table: "training_labels",
                columns: new[] { "label", "labeled_at" });
        }
    }
}
