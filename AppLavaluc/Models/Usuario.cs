using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace AppLavaluc.Models
{
    public class Usuario
    {
        [Key]
        public int UsuarioID { get; set; }

        [Required(ErrorMessage = "El nombre de usuario es obligatorio")]
        [StringLength(50)]
        public string NombreUsuario { get; set; }

        [Required(ErrorMessage = "El nombre completo es obligatorio")]
        [StringLength(100)]
        [Display(Name = "Nombre Completo")]
        public string NombreCompleto { get; set; }

        [Required(ErrorMessage = "El correo electrónico es obligatorio")]
        [EmailAddress(ErrorMessage = "El formato del correo no es válido")]
        [StringLength(100)]
        public string Email { get; set; }

        [ValidateNever]
        public string? PasswordHash { get; set; }

        [Display(Name = "Rol")]
        [StringLength(30)]
        public string Rol { get; set; } = "Empleado"; // Por defecto, se puede expandir a "Admin", etc.

        [Display(Name = "Estado")]
        public bool Activo { get; set; } = true;

        public DateTime FechaCreacion { get; set; } = DateTime.Now;
    }
}
