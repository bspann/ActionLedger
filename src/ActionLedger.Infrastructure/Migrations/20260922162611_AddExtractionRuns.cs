using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActionLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractionRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "action_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    old_value = table.Column<string>(type: "text", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_revisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "extraction_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    meeting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meeting_notes_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    started_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    schema_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    warnings = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraction_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "proposed_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extraction_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    suggested_owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    suggested_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    source_excerpt = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    review_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposed_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_proposed_actions_extraction_runs_extraction_run_id",
                        column: x => x.extraction_run_id,
                        principalTable: "extraction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_action_revisions_target_type_target_id_sequence",
                table: "action_revisions",
                columns: new[] { "target_type", "target_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_extraction_runs_meeting_id",
                table: "extraction_runs",
                column: "meeting_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposed_actions_extraction_run_id_ordinal",
                table: "proposed_actions",
                columns: new[] { "extraction_run_id", "ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_revisions");

            migrationBuilder.DropTable(
                name: "proposed_actions");

            migrationBuilder.DropTable(
                name: "extraction_runs");
        }
    }
}
