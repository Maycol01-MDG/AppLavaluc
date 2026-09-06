using System.ComponentModel.DataAnnotations;

namespace AppLavaluc.Models
{
    public class Categoria
    {
        [Key]
        public int CategoriaID { get; set; }

        [Required(ErrorMessage = "El nombre de la categoría es obligatorio")]
        [Display(Name = "Categoría de Servicio")]
        public string NombreCategoria { get; set; } = string.Empty;

        public ICollection<Servicio>? Servicios { get; set; }
    }
}
