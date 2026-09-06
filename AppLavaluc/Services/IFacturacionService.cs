using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AppLavaluc.Models;

namespace AppLavaluc.Services
{
    public interface IFacturacionService
    {
        Task<(bool Ok, Comprobante? Comprobante, string? Error)> EmitirComprobanteAsync(EmitirComprobanteRequest request);
        Task<(bool Ok, Comprobante? NotaCredito, string? Error)> EmitirNotaCreditoAsync(EmitirNotaCreditoRequest request);
        Task<(byte[]? Archivo, string ContentType, string NombreArchivo, string? Error)> ObtenerPdfAsync(int comprobanteId);
        Task<(string? Xml, string NombreArchivo, string? Error)> ObtenerXmlAsync(int comprobanteId);
        Task<int> ObtenerSiguienteCorrelativoAsync(string tipoDoc, string serie);
        Task<Comprobante?> ObtenerPorOrdenIdAsync(int ordenId);
        Task<Comprobante?> ObtenerPorIdAsync(int comprobanteId);
        Task<List<Comprobante>> ListarComprobantesAsync(string? tipoDoc = null, string? estado = null, string? buscar = null, DateTime? fechaInicio = null, DateTime? fechaFin = null);
    }
}
