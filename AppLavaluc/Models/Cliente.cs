using System.ComponentModel.DataAnnotations;

namespace AppLavaluc.Models
{
    public class Cliente
    {
        [Key]
        public int ClienteID { get; set; }

        [Required(ErrorMessage = "El nombre del cliente es obligatorio")]
        [StringLength(100)]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Apellidos")]
        public string? Apellidos { get; set; }

        [Phone(ErrorMessage = "Ingrese un teléfono válido")]
        [StringLength(20)]
        public string? Telefono { get; set; }

        [EmailAddress(ErrorMessage = "Ingrese un email válido")]
        [StringLength(150)]
        public string? Email { get; set; }
        public string NombreCompleto =>
            string.IsNullOrWhiteSpace(Apellidos)
                ? Nombre
                : $"{Nombre} {Apellidos}";

        public ICollection<Orden>? Ordenes { get; set; }
    }
}