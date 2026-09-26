using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CongresoApi.Migrations
{
    /// <inheritdoc />
    public partial class AgregarQrEnviadoAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "QrEnviadoAt",
                table: "Asistentes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QrEnviadoAt",
                table: "Asistentes");
        }
    }
}
