using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActionLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "meetings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    meeting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    attendees = table.Column<string[]>(type: "text[]", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meetings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meeting_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    meeting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    saved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_notes_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_notes_meeting_id",
                table: "meeting_notes",
                column: "meeting_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meetings_title_meeting_date",
                table: "meetings",
                columns: new[] { "title", "meeting_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meeting_notes");

            migrationBuilder.DropTable(
                name: "meetings");
        }
    }
}
