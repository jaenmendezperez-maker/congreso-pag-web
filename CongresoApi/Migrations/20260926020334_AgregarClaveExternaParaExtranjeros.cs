using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CongresoApi.Migrations
{
    /// <inheritdoc />
    public partial class AgregarClaveExternaParaExtranjeros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Asistentes_Correo",
                table: "Asistentes");

            migrationBuilder.AddColumn<string>(
                name: "ClaveExterna",
                table: "Asistentes",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Asistentes_ClaveExterna",
                table: "Asistentes",
                column: "ClaveExterna",
                unique: true,
                filter: "\"ClaveExterna\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Asistentes_Correo",
                table: "Asistentes",
                column: "Correo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Asistentes_ClaveExterna",
                table: "Asistentes");

            migrationBuilder.DropIndex(
                name: "IX_Asistentes_Correo",
                table: "Asistentes");

            migrationBuilder.DropColumn(
                name: "ClaveExterna",
                table: "Asistentes");

            migrationBuilder.CreateIndex(
                name: "IX_Asistentes_Correo",
                table: "Asistentes",
                column: "Correo",
                unique: true);
        }
    }
}
