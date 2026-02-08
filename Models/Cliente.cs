using System.ComponentModel.DataAnnotations;

namespace AppLavaluc.Models
{
    public class Cliente
    {
        [Key]
        public int ClienteID { get; set; }

        [Required(ErrorMessage = "El nombre del cliente es obligatorio")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; }

        [Display(Name = "Apellidos")]
        public string? Apellidos { get; set; }

        public string? Telefono { get; set; }
        [EmailAddress]
        public string? Email { get; set; }


        // Propiedad calculada que devuelve el nombre completo.
        // Si mañana agregas Apellidos, puedes cambiar la concatenación.
        public string NombreCompleto => Nombre; // Por ahora solo Nombre
        public ICollection<Orden>? Ordenes { get; set; }
    }
}
