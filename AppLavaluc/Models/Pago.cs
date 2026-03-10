using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Models
{
    public class Pago
    {
        [Key]
        public int PagoID { get; set; }

        [Required]
        public int OrdenID { get; set; }

        [ForeignKey("OrdenID")]
        public Orden? Orden { get; set; }

        [Required]
        [Display(Name = "Fecha de Pago")]
        public DateTime FechaPago { get; set; } = DateTime.Now;

        [Required]
        [Precision(10, 2)]
        [Display(Name = "Monto (S/.)")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0")]
        public decimal Monto { get; set; }

        [StringLength(100)]
        [Display(Name = "Método de Pago")]
        public string? MetodoPago { get; set; } // Efectivo, Yape, Plin, Tarjeta, etc.

        [StringLength(200)]
        public string? Notas { get; set; }
    }
}
