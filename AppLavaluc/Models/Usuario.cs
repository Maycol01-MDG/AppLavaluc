using System.ComponentModel.DataAnnotations;

namespace AppLavaluc.Models
{
    public class Usuario
    {
        [Key]
        public int UsuarioID { get; set; }

        [Required(ErrorMessage = "El nombre de usuario es obligatorio")]
        [StringLength(50)]
        public string NombreUsuario { get; set; }

        [Required(ErrorMessage = "El correo electrónico es obligatorio")]
        [EmailAddress(ErrorMessage = "El formato del correo no es válido")]
        [StringLength(100)]
        public string Email { get; set; }

        [Required]
        public string PasswordHash { get; set; }

        [Display(Name = "Rol")]
        [StringLength(30)]
        public string Rol { get; set; } = "Empleado"; // Por defecto, se puede expandir a "Admin", etc.

        public DateTime FechaCreacion { get; set; } = DateTime.Now;
    }
}
