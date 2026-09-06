using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AppLavaluc.Services
{
    // ─────────────────────────────────────────────────────────────
    // AUTENTICACIÓN
    // ─────────────────────────────────────────────────────────────
    public class LoginApiRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginApiResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        public string? ObtenerToken() => !string.IsNullOrWhiteSpace(Token) ? Token : AccessToken;
    }

    // ─────────────────────────────────────────────────────────────
    // COMPROBANTE UBL 2.1 (FACTURAS Y BOLETAS)
    // ─────────────────────────────────────────────────────────────
    public class InvoiceRequest
    {
        [JsonPropertyName("ublVersion")]
        public string UblVersion { get; set; } = "2.1";

        [JsonPropertyName("tipoDoc")]
        public string TipoDoc { get; set; } = "03"; // 01: Factura, 03: Boleta

        [JsonPropertyName("tipoOperacion")]
        public string TipoOperacion { get; set; } = "0101";

        [JsonPropertyName("serie")]
        public string Serie { get; set; } = string.Empty;

        [JsonPropertyName("correlativo")]
        public string Correlativo { get; set; } = string.Empty;

        [JsonPropertyName("fechaEmision")]
        public string FechaEmision { get; set; } = string.Empty;

        [JsonPropertyName("formaPago")]
        public FormaPagoDto FormaPago { get; set; } = new();

        [JsonPropertyName("tipoMoneda")]
        public string TipoMoneda { get; set; } = "PEN";

        [JsonPropertyName("company")]
        public CompanyDto Company { get; set; } = new();

        [JsonPropertyName("client")]
        public ClientDto Client { get; set; } = new();

        [JsonPropertyName("details")]
        public List<InvoiceDetailDto> Details { get; set; } = new();
    }

    public class FormaPagoDto
    {
        [JsonPropertyName("moneda")]
        public string Moneda { get; set; } = "PEN";

        [JsonPropertyName("tipo")]
        public string Tipo { get; set; } = "Contado";
    }

    public class CompanyDto
    {
        [JsonPropertyName("ruc")]
        public string Ruc { get; set; } = string.Empty;

        [JsonPropertyName("razonSocial")]
        public string RazonSocial { get; set; } = string.Empty;

        [JsonPropertyName("nombreComercial")]
        public string? NombreComercial { get; set; }

        [JsonPropertyName("address")]
        public AddressDto Address { get; set; } = new();
    }

    public class AddressDto
    {
        [JsonPropertyName("ubigueo")]
        public string Ubigueo { get; set; } = "150101";

        [JsonPropertyName("departamento")]
        public string Departamento { get; set; } = "LIMA";

        [JsonPropertyName("provincia")]
        public string Provincia { get; set; } = "LIMA";

        [JsonPropertyName("distrito")]
        public string Distrito { get; set; } = "LIMA";

        [JsonPropertyName("urbanizacion")]
        public string Urbanizacion { get; set; } = "-";

        [JsonPropertyName("direccion")]
        public string Direccion { get; set; } = string.Empty;

        [JsonPropertyName("codLocal")]
        public string CodLocal { get; set; } = "0000";
    }

    public class ClientDto
    {
        [JsonPropertyName("tipoDoc")]
        public string TipoDoc { get; set; } = "1"; // 1: DNI, 6: RUC, 0: Sin Documento

        [JsonPropertyName("numDoc")]
        public string NumDoc { get; set; } = string.Empty;

        [JsonPropertyName("rznSocial")]
        public string RznSocial { get; set; } = string.Empty;

        [JsonPropertyName("address")]
        public AddressDto? Address { get; set; }
    }

    public class InvoiceDetailDto
    {
        [JsonPropertyName("tipAfeIgv")]
        public int TipAfeIgv { get; set; } = 10; // 10: Gravado - Operación Onerosa

        [JsonPropertyName("codProducto")]
        public string CodProducto { get; set; } = "P001";

        [JsonPropertyName("unidad")]
        public string Unidad { get; set; } = "NIU";

        [JsonPropertyName("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        [JsonPropertyName("cantidad")]
        public decimal Cantidad { get; set; }

        [JsonPropertyName("mtoValorUnitario")]
        public decimal MtoValorUnitario { get; set; }

        [JsonPropertyName("mtoValorVenta")]
        public decimal MtoValorVenta { get; set; }

        [JsonPropertyName("mtoBaseIgv")]
        public decimal MtoBaseIgv { get; set; }

        [JsonPropertyName("porcentajeIgv")]
        public decimal PorcentajeIgv { get; set; } = 18;

        [JsonPropertyName("igv")]
        public decimal Igv { get; set; }

        [JsonPropertyName("totalImpuestos")]
        public decimal TotalImpuestos { get; set; }

        [JsonPropertyName("mtoPrecioUnitario")]
        public decimal MtoPrecioUnitario { get; set; }
    }

    // ─────────────────────────────────────────────────────────────
    // PETICIÓN PARA EMITIR DESDE LA INTERFAZ
    // ─────────────────────────────────────────────────────────────
    public class EmitirComprobanteRequest
    {
        public int OrdenId { get; set; }
        public string TipoDoc { get; set; } = "03"; // 01: Factura, 03: Boleta
        public string? Serie { get; set; }
        public string? NumDocCliente { get; set; }
        public string? RznSocialCliente { get; set; }
        public string? DireccionCliente { get; set; }
    }

    // ─────────────────────────────────────────────────────────────
    // NOTAS DE CRÉDITO Y DÉBITO (UBL 2.1)
    // ─────────────────────────────────────────────────────────────
    public class NoteRequest
    {
        [JsonPropertyName("ublVersion")]
        public string UblVersion { get; set; } = "2.1";

        [JsonPropertyName("tipoDoc")]
        public string TipoDoc { get; set; } = "07"; // 07: Nota de Crédito, 08: Nota de Débito

        [JsonPropertyName("serie")]
        public string Serie { get; set; } = string.Empty;

        [JsonPropertyName("correlativo")]
        public string Correlativo { get; set; } = string.Empty;

        [JsonPropertyName("fechaEmision")]
        public string FechaEmision { get; set; } = string.Empty;

        [JsonPropertyName("tipDocAfectado")]
        public string TipDocAfectado { get; set; } = "01"; // 01: Factura, 03: Boleta

        [JsonPropertyName("numDocfectado")]
        public string NumDocfectado { get; set; } = string.Empty;

        [JsonPropertyName("codMotivo")]
        public string CodMotivo { get; set; } = "01";

        [JsonPropertyName("desMotivo")]
        public string DesMotivo { get; set; } = "Anulacion de la operacion";

        [JsonPropertyName("formaPago")]
        public FormaPagoDto FormaPago { get; set; } = new();

        [JsonPropertyName("tipoMoneda")]
        public string TipoMoneda { get; set; } = "PEN";

        [JsonPropertyName("company")]
        public CompanyDto Company { get; set; } = new();

        [JsonPropertyName("client")]
        public ClientDto Client { get; set; } = new();

        [JsonPropertyName("details")]
        public List<InvoiceDetailDto> Details { get; set; } = new();
    }

    public class EmitirNotaCreditoRequest
    {
        public int ComprobanteId { get; set; }
        public string CodMotivo { get; set; } = "01";
        public string? DesMotivo { get; set; }
        public string? Serie { get; set; }
    }

    public static class CatalogoSunatNotas
    {
        public static readonly Dictionary<string, string> MotivosNotaCredito = new()
        {
            { "01", "Anulación de la operación" },
            { "02", "Anulación por error en el RUC" },
            { "03", "Corrección por error en la descripción" },
            { "04", "Descuento global" },
            { "05", "Descuento por ítem" },
            { "06", "Devolución total" },
            { "07", "Devolución por ítem" },
            { "08", "Bonificación" },
            { "09", "Disminución en el valor" },
            { "10", "Otros conceptos" }
        };
    }
}
