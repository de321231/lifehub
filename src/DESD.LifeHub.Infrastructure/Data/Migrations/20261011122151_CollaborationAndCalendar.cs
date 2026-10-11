using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DESD.LifeHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CollaborationAndCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorCategory",
                table: "calendar_events",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurrenceFrequency",
                table: "calendar_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceInterval",
                table: "calendar_events",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecurrenceUntil",
                table: "calendar_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoomId",
                table: "calendar_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "absence_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_absence_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_absence_requests_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_absence_requests_users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_absence_requests_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event_participants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Response = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsOrganizer = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_event_participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_calendar_event_participants_calendar_events_CalendarEventId",
                        column: x => x.CalendarEventId,
                        principalTable: "calendar_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_calendar_event_participants_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_shares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SharedWithUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanEdit = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_shares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_calendar_shares_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_calendar_shares_users_SharedWithUserId",
                        column: x => x.SharedWithUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_announcements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    PublishAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_announcements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_company_announcements_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_company_announcements_users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    ColorCategory = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rooms_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_attachments_tasks_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_attachments_users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_comments_tasks_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_comments_users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_events_RecurrenceFrequency",
                table: "calendar_events",
                column: "RecurrenceFrequency");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_events_RoomId",
                table: "calendar_events",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_absence_requests_DecidedByUserId",
                table: "absence_requests",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_absence_requests_OrganizationId_StartDate_EndDate",
                table: "absence_requests",
                columns: new[] { "OrganizationId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_absence_requests_UserId",
                table: "absence_requests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_event_participants_CalendarEventId_UserId",
                table: "calendar_event_participants",
                columns: new[] { "CalendarEventId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_calendar_event_participants_UserId",
                table: "calendar_event_participants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_shares_OwnerUserId_SharedWithUserId",
                table: "calendar_shares",
                columns: new[] { "OwnerUserId", "SharedWithUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_calendar_shares_SharedWithUserId",
                table: "calendar_shares",
                column: "SharedWithUserId");

            migrationBuilder.CreateIndex(
                name: "IX_company_announcements_AuthorUserId",
                table: "company_announcements",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_company_announcements_OrganizationId_PublishAt",
                table: "company_announcements",
                columns: new[] { "OrganizationId", "PublishAt" });

            migrationBuilder.CreateIndex(
                name: "IX_rooms_OrganizationId_IsActive",
                table: "rooms",
                columns: new[] { "OrganizationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_task_attachments_StorageKey",
                table: "task_attachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_attachments_TaskItemId",
                table: "task_attachments",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_task_attachments_UploadedByUserId",
                table: "task_attachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_AuthorUserId",
                table: "task_comments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_TaskItemId_CreatedAt",
                table: "task_comments",
                columns: new[] { "TaskItemId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_calendar_events_rooms_RoomId",
                table: "calendar_events",
                column: "RoomId",
                principalTable: "rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_calendar_events_rooms_RoomId",
                table: "calendar_events");

            migrationBuilder.DropTable(
                name: "absence_requests");

            migrationBuilder.DropTable(
                name: "calendar_event_participants");

            migrationBuilder.DropTable(
                name: "calendar_shares");

            migrationBuilder.DropTable(
                name: "company_announcements");

            migrationBuilder.DropTable(
                name: "rooms");

            migrationBuilder.DropTable(
                name: "task_attachments");

            migrationBuilder.DropTable(
                name: "task_comments");

            migrationBuilder.DropIndex(
                name: "IX_calendar_events_RecurrenceFrequency",
                table: "calendar_events");

            migrationBuilder.DropIndex(
                name: "IX_calendar_events_RoomId",
                table: "calendar_events");

            migrationBuilder.DropColumn(
                name: "ColorCategory",
                table: "calendar_events");

            migrationBuilder.DropColumn(
                name: "RecurrenceFrequency",
                table: "calendar_events");

            migrationBuilder.DropColumn(
                name: "RecurrenceInterval",
                table: "calendar_events");

            migrationBuilder.DropColumn(
                name: "RecurrenceUntil",
                table: "calendar_events");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "calendar_events");
        }
    }
}
