using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Models
{
    public class Servicio
    {
        [Key]
        public int ServicioID { get; set; }

        [Required(ErrorMessage = "El nombre del servicio es obligatorio")]
        [Display(Name = "Tipo de Servicio")]
        public string NombreServicio { get; set; }

        [Display(Name = "Descripción")]
        public string? Descripcion { get; set; }

        [Required(ErrorMessage = "Debe asignar un precio")]
        [Precision(10, 2)]
        public decimal Precio { get; set; }

        [Required(ErrorMessage = "Debe seleccionar una categoría")]
        [ForeignKey("Categoria")]
        public int CategoriaID { get; set; }
        public Categoria? Categoria { get; set; }

        [Required(ErrorMessage = "Debe especificar la unidad de medida (ej. Kilo, Unidad, Pieza, etc.)")]
        [StringLength(30)]
        [Display(Name = "Unidad de Medida")]
        public string UnidadMedida { get; set; }

        public ICollection<DetalleOrden>? DetallesOrden { get; set; }
    }
}
