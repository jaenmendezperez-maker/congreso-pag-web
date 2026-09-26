using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CongresoApi.Migrations
{
    /// <inheritdoc />
    public partial class InicialAsistentesYEscaneos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Asistentes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Origen = table.Column<int>(type: "integer", nullable: false),
                    Matricula = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Correo = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CorreoAlterno = table.Column<string>(type: "text", nullable: true),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    ApellidoPaterno = table.Column<string>(type: "text", nullable: true),
                    ApellidoMaterno = table.Column<string>(type: "text", nullable: true),
                    Universidad = table.Column<string>(type: "text", nullable: true),
                    Seccion = table.Column<string>(type: "text", nullable: true),
                    EstadoPais = table.Column<string>(type: "text", nullable: true),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EntregadoAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EntregadoPor = table.Column<string>(type: "text", nullable: true),
                    Estacion = table.Column<string>(type: "text", nullable: true),
                    CreadoEnSistema = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualizadoEnSistema = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Asistentes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Escaneos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AsistenteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Resultado = table.Column<int>(type: "integer", nullable: false),
                    Estacion = table.Column<string>(type: "text", nullable: true),
                    RealizadoPor = table.Column<string>(type: "text", nullable: true),
                    Motivo = table.Column<string>(type: "text", nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Escaneos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Escaneos_Asistentes_AsistenteId",
                        column: x => x.AsistenteId,
                        principalTable: "Asistentes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Asistentes_Correo",
                table: "Asistentes",
                column: "Correo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Asistentes_Matricula",
                table: "Asistentes",
                column: "Matricula",
                unique: true,
                filter: "\"Matricula\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Escaneos_AsistenteId",
                table: "Escaneos",
                column: "AsistenteId");

            migrationBuilder.CreateIndex(
                name: "IX_Escaneos_CreadoEn",
                table: "Escaneos",
                column: "CreadoEn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Escaneos");

            migrationBuilder.DropTable(
                name: "Asistentes");
        }
    }
}
