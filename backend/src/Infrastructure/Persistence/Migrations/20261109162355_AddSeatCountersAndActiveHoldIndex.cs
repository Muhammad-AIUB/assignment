using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatCountersAndActiveHoldIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfirmedSeats",
                table: "classes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HeldSeats",
                table: "classes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill counters from any rows that already exist.
            migrationBuilder.Sql("""
                UPDATE classes c SET
                  "HeldSeats" = (SELECT count(*) FROM holds h WHERE h."ClassId" = c."Id" AND h."Status" = 'Held' AND h."ExpiresAt" > now()),
                  "ConfirmedSeats" = (SELECT count(*) FROM holds h WHERE h."ClassId" = c."Id" AND h."Status" = 'Confirmed');
                UPDATE holds SET "Status" = 'Expired' WHERE "Status" = 'Held' AND "ExpiresAt" <= now();
                """);

            migrationBuilder.CreateIndex(
                name: "ux_holds_active_student_class",
                table: "holds",
                columns: new[] { "ClassId", "StudentId" },
                unique: true,
                filter: "\"Status\" IN ('Held', 'Confirmed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_classes_seats_not_oversold",
                table: "classes",
                sql: "\"HeldSeats\" >= 0 AND \"ConfirmedSeats\" >= 0 AND \"HeldSeats\" + \"ConfirmedSeats\" <= \"TotalSeats\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_holds_active_student_class",
                table: "holds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_classes_seats_not_oversold",
                table: "classes");

            migrationBuilder.DropColumn(
                name: "ConfirmedSeats",
                table: "classes");

            migrationBuilder.DropColumn(
                name: "HeldSeats",
                table: "classes");
        }
    }
}
