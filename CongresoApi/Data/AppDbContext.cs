using CongresoApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CongresoApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Asistente> Asistentes => Set<Asistente>();
    public DbSet<Escaneo> Escaneos => Set<Escaneo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var asistente = modelBuilder.Entity<Asistente>();

        // El correo YA NO es único por sí solo: varias personas distintas a veces
        // comparten el mismo correo (ej. un profesor registra a su grupo con su propio
        // correo). La unicidad real para extranjeros vive en ClaveExterna (correo+nombre).
        asistente.HasIndex(a => a.Correo); // índice normal, no único — solo para búsquedas rápidas

        // ClaveExterna única solo entre quienes la tienen (los de USEP la dejan null,
        // porque ahí la Matrícula ya identifica a la persona de forma inequívoca).
        asistente.HasIndex(a => a.ClaveExterna)
            .IsUnique()
            .HasFilter("\"ClaveExterna\" IS NOT NULL");

        // Matrícula única solo entre los que la tienen (los extranjeros la dejan null)
        asistente.HasIndex(a => a.Matricula)
            .IsUnique()
            .HasFilter("\"Matricula\" IS NOT NULL");

        asistente.Property(a => a.Correo).HasMaxLength(320);
        asistente.Property(a => a.Matricula).HasMaxLength(50);
        asistente.Property(a => a.ClaveExterna).HasMaxLength(400);

        var escaneo = modelBuilder.Entity<Escaneo>();
        escaneo.HasOne(e => e.Asistente)
            .WithMany()
            .HasForeignKey(e => e.AsistenteId)
            .OnDelete(DeleteBehavior.SetNull);

        escaneo.HasIndex(e => e.CreadoEn);
    }
}