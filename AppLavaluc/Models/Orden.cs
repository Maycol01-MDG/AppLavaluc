using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Models
{
    public class Orden
    {
        [Key]
        public int OrdenID { get; set; }

        [Required(ErrorMessage = "Debe seleccionar un cliente")]
        [Display(Name = "Cliente")]
        public int ClienteID { get; set; }

        [ForeignKey("ClienteID")]
        public Cliente? Cliente { get; set; }

        [Display(Name = "Fecha de Recepción")]
        [DataType(DataType.Date)]
        public DateTime FechaRecepcion { get; set; } = DateTime.Now;

        [Display(Name = "Fecha de Entrega Estimada")]
        [DataType(DataType.Date)]
        public DateTime? FechaEntregaEstimada { get; set; }

        [Required(ErrorMessage = "Debe especificar un estado")]
        [StringLength(50)]
        public string Estado { get; set; } = "Recibido";

        [Precision(10, 2)]
        [Display(Name = "Monto Total (S/.)")]
        [Range(0, double.MaxValue, ErrorMessage = "El monto debe ser positivo")]
        public decimal MontoTotal { get; set; }

        [StringLength(30)]
        [Display(Name = "Tipo de Entrega")]
        public string? TipoEntrega { get; set; }

        [Phone(ErrorMessage = "Ingrese un teléfono válido")]
        [Display(Name = "Teléfono de Contacto")]
        public string? Telefono { get; set; }

        [Display(Name = "Observaciones")]
        [StringLength(500)]
        public string? Observaciones { get; set; }

        public decimal MontoPagado { get; set; }
        public decimal SaldoPendiente { get; set; }

        public string EstadoPago { get; set; } // Pendiente | Parcial | Pagado
        public string EstadoRecojo { get; set; } // Pendiente | Recogido



        public ICollection<DetalleOrden>? Detalles { get; set; }

        [NotMapped]
        [Display(Name = "Cantidad de Servicios")]
        public int CantidadServicios => Detalles?.Count ?? 0;
    }
}