using System.ComponentModel.DataAnnotations;

namespace AppLavaluc.Models
{
    public class Reporte
    {
        // Filtros aplicados
        [Display(Name = "Fecha Inicio")]
        [DataType(DataType.Date)]
        public DateTime FechaInicio { get; set; }

        [Display(Name = "Fecha Fin")]
        [DataType(DataType.Date)]
        public DateTime FechaFin { get; set; }

        public string TipoFiltro { get; set; } // "Hoy", "Semana", "Mes", "Personalizado"

        // KPIs (Indicadores Clave)
        [Display(Name = "Total Generado")]
        public decimal TotalVentas { get; set; } // Suma de MontoTotal (lo que vale el trabajo)

        [Display(Name = "Dinero en Caja")]
        public decimal TotalRecaudado { get; set; } // Suma de MontoPagado (lo que entró realmente)

        [Display(Name = "Por Cobrar")]
        public decimal TotalDeuda { get; set; } // Suma de SaldoPendiente (lo que deben los clientes)

        public int CantidadOrdenes { get; set; }
        public int CantidadPrendas { get; set; } // Opcional, si quisieras contar prendas

        // Lista detallada para la tabla
        public List<Orden> Detalles { get; set; } = new List<Orden>();
        public List<Pago> PagosDetalle { get; set; } = new List<Pago>();
    }
}