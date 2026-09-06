using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Models
{
    public class Comprobante
    {
        [Key]
        public int ComprobanteID { get; set; }

        [Required]
        [ForeignKey("Orden")]
        public int OrdenID { get; set; }

        public Orden? Orden { get; set; }

        [Required]
        [StringLength(2)]
        [Display(Name = "Tipo de Documento")]
        public string TipoDoc { get; set; } = "03"; // 01: Factura, 03: Boleta

        [Required]
        [StringLength(10)]
        [Display(Name = "Serie")]
        public string Serie { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Correlativo")]
        public int Correlativo { get; set; }

        [Required]
        [Display(Name = "Fecha de Emisión")]
        public DateTime FechaEmision { get; set; } = DateTime.Now;

        [StringLength(11)]
        [Display(Name = "RUC Emisor")]
        public string? RucEmisor { get; set; }

        [StringLength(200)]
        [Display(Name = "Razón Social Emisor")]
        public string? RazonSocialEmisor { get; set; }

        [StringLength(2)]
        [Display(Name = "Tipo Doc. Cliente")]
        public string TipoDocCliente { get; set; } = "1"; // 1: DNI, 6: RUC, 0: Sin Doc

        [StringLength(20)]
        [Display(Name = "N° Documento Cliente")]
        public string? NumDocCliente { get; set; }

        [StringLength(200)]
        [Display(Name = "Nombre / Razón Social Cliente")]
        public string? RznSocialCliente { get; set; }

        [StringLength(300)]
        [Display(Name = "Dirección Cliente")]
        public string? DireccionCliente { get; set; }

        [Precision(10, 2)]
        [Display(Name = "Op. Gravadas (S/.)")]
        public decimal MontoOperacionesGravadas { get; set; }

        [Precision(10, 2)]
        [Display(Name = "IGV 18% (S/.)")]
        public decimal MontoIgv { get; set; }

        [Precision(10, 2)]
        [Display(Name = "Total (S/.)")]
        public decimal MontoTotal { get; set; }

        [StringLength(50)]
        [Display(Name = "Estado SUNAT")]
        public string EstadoSunat { get; set; } = "Pendiente"; // Aceptado, Rechazado, Pendiente, Error

        [StringLength(20)]
        [Display(Name = "Código Respuesta")]
        public string? CodigoRespuesta { get; set; }

        [StringLength(500)]
        [Display(Name = "Respuesta / Observaciones SUNAT")]
        public string? MensajeSunat { get; set; }

        [StringLength(200)]
        [Display(Name = "Hash / Firma CDR")]
        public string? HashCdr { get; set; }

        [StringLength(100)]
        [Display(Name = "Nombre de Archivo")]
        public string? NombreArchivo { get; set; }

        public string? XmlFirmado { get; set; }

        // ─────────────────────────────────────────────────────────────
        // CAMPOS PARA NOTAS DE CRÉDITO / DÉBITO (UBL 2.1)
        // ─────────────────────────────────────────────────────────────
        [StringLength(2)]
        [Display(Name = "Tipo Doc. Afectado")]
        public string? TipDocAfectado { get; set; } // 01: Factura, 03: Boleta

        [StringLength(30)]
        [Display(Name = "N° Doc. Afectado")]
        public string? NumDocAfectado { get; set; } // Ej: F001-000001

        [StringLength(10)]
        [Display(Name = "Código Motivo SUNAT")]
        public string? CodMotivo { get; set; } // Catálogo 9: 01 Anulación, 02 Corrección RUC, etc.

        [StringLength(250)]
        [Display(Name = "Descripción Motivo")]
        public string? DesMotivo { get; set; }

        public int? ComprobanteReferenciaID { get; set; }

        [ForeignKey("ComprobanteReferenciaID")]
        public Comprobante? ComprobanteReferencia { get; set; }

        [NotMapped]
        public string NumeroCompleto => $"{Serie}-{Correlativo:D6}";

        [NotMapped]
        public bool EsNotaCredito => TipoDoc == "07";

        [NotMapped]
        public bool EsNotaDebito => TipoDoc == "08";

        [NotMapped]
        public string TipoDocDescripcion => TipoDoc switch
        {
            "01" => "Factura Electrónica",
            "03" => "Boleta de Venta",
            "07" => "Nota de Crédito",
            "08" => "Nota de Débito",
            _ => "Comprobante"
        };

        [NotMapped]
        public string BadgeClass => TipoDoc switch
        {
            "01" => "bg-primary",
            "03" => "bg-info text-dark",
            "07" => "bg-warning text-dark",
            "08" => "bg-danger",
            _ => "bg-secondary"
        };
    }
}
