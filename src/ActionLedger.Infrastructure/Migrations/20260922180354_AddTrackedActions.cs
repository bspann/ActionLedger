using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActionLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "proposed_actions",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by_user_id",
                table: "proposed_actions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "proposed_actions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tracked_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracked_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_tracked_actions_proposed_actions_proposed_action_id",
                        column: x => x.proposed_action_id,
                        principalTable: "proposed_actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tracked_actions_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tracked_actions_due_date_status",
                table: "tracked_actions",
                columns: new[] { "due_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tracked_actions_owner_user_id",
                table: "tracked_actions",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracked_actions_proposed_action_id",
                table: "tracked_actions",
                column: "proposed_action_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tracked_actions");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "proposed_actions");

            migrationBuilder.DropColumn(
                name: "decided_by_user_id",
                table: "proposed_actions");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "proposed_actions");
        }
    }
}
