using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Models
{
    public class DetalleOrden
    {
        [Key]
        public int DetalleOrdenID { get; set; }

        [Required]
        [ForeignKey("Orden")]
        public int OrdenID { get; set; }

        public Orden? Orden { get; set; }

        [Required(ErrorMessage = "Debe seleccionar un servicio")]
        [ForeignKey("Servicio")]
        public int ServicioID { get; set; }

        public Servicio? Servicio { get; set; }

        [Required(ErrorMessage = "Ingrese la cantidad")]
        [Range(1, 1000, ErrorMessage = "Debe ser al menos 1")]
        public int Cantidad { get; set; }

        [Precision(10, 2)]
        [Range(0, 9999.99)]
        public decimal PrecioUnitario { get; set; }

        [Precision(10, 2)]
        [Range(0, 9999.99)]
        public decimal Descuento { get; set; }

        [Precision(10, 2)]
        [Display(Name = "Total (S/.)")]
        public decimal Total => (Cantidad * PrecioUnitario) - Descuento;
    }
}
