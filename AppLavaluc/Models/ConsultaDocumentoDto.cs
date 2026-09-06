using System.Text.Json.Serialization;

namespace AppLavaluc.Models
{
    public enum TipoDocumento
    {
        Dni = 8,
        Ruc = 11
    }

    public class ConsultaDocumentoRespuesta
    {
        public bool Exito { get; set; }
        public string? MensajeError { get; set; }
        public TipoDocumento? Tipo { get; set; }
        public string NumeroDocumento { get; set; } = string.Empty;

        // Datos unificados para formulario
        public string NombreORazonSocial { get; set; } = string.Empty;
        public string? Apellidos { get; set; }
        public string? Telefono { get; set; }
        public string? Direccion { get; set; }
        public string? Estado { get; set; }
        public string? Condicion { get; set; }
        public string Origen { get; set; } = "api"; // "local", "reniec", "sunat_latinfo", "nuevo"
    }

    // DTO para mapear respuesta de api.latinfo.dev/pe/kyb/{ruc}
    public class LatinfoKybResponse
    {
        [JsonPropertyName("identity")]
        public LatinfoIdentity? Identity { get; set; }

        [JsonPropertyName("activity")]
        public LatinfoActivity? Activity { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        // Compatibilidad con DTOs planos alternativos
        [JsonPropertyName("legalName")]
        public string? LegalName { get; set; }

        [JsonPropertyName("tradeName")]
        public string? TradeName { get; set; }

        [JsonPropertyName("fiscalAddress")]
        public string? FiscalAddress { get; set; }
    }

    public class LatinfoIdentity
    {
        [JsonPropertyName("ruc")]
        public string? Ruc { get; set; }

        [JsonPropertyName("razon_social")]
        public string? RazonSocial { get; set; }

        [JsonPropertyName("estado")]
        public string? Estado { get; set; }

        [JsonPropertyName("condicion")]
        public string? Condicion { get; set; }

        [JsonPropertyName("ubigeo")]
        public string? Ubigeo { get; set; }

        [JsonPropertyName("tipo_via")]
        public string? TipoVia { get; set; }

        [JsonPropertyName("nombre_via")]
        public string? NombreVia { get; set; }

        [JsonPropertyName("codigo_zona")]
        public string? CodigoZona { get; set; }

        [JsonPropertyName("tipo_zona")]
        public string? TipoZona { get; set; }

        [JsonPropertyName("numero")]
        public string? Numero { get; set; }

        [JsonPropertyName("interior")]
        public string? Interior { get; set; }

        [JsonPropertyName("lote")]
        public string? Lote { get; set; }

        [JsonPropertyName("manzana")]
        public string? Manzana { get; set; }
    }

    public class LatinfoActivity
    {
        [JsonPropertyName("departamento")]
        public string? Departamento { get; set; }

        [JsonPropertyName("provincia")]
        public string? Provincia { get; set; }

        [JsonPropertyName("distrito")]
        public string? Distrito { get; set; }

        [JsonPropertyName("tipo_contribuyente")]
        public string? TipoContribuyente { get; set; }
    }
}
