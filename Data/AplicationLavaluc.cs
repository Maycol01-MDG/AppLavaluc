using Microsoft.EntityFrameworkCore;
using AppLavaluc.Models;

namespace AppLavaluc.Data
{
    public class LavanderiaContext : DbContext
    {
        public LavanderiaContext(DbContextOptions<LavanderiaContext> options) : base(options) { }

        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<Categoria> Categorias { get; set; }
        public DbSet<Servicio> Servicios { get; set; }
        public DbSet<Orden> Ordenes { get; set; }
        public DbSet<DetalleOrden> DetallesOrden { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Orden>()
                .HasOne(o => o.Cliente)
                .WithMany(c => c.Ordenes)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DetalleOrden>()
                .HasOne(d => d.Orden)
                .WithMany(o => o.Detalles)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DetalleOrden>()
                .HasOne(d => d.Servicio)
                .WithMany(s => s.DetallesOrden)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
