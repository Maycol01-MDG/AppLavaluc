using System;
using System.Text;
using System.Threading.Tasks;
using AppLavaluc.Data;
using AppLavaluc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppLavaluc.Controllers
{
    [Authorize]
    public class FacturaController : Controller
    {
        private readonly IFacturacionService _facturacionService;
        private readonly LavanderiaContext _db;
        private readonly ILogger<FacturaController> _logger;

        public FacturaController(
            IFacturacionService facturacionService,
            LavanderiaContext db,
            ILogger<FacturaController> logger)
        {
            _facturacionService = facturacionService;
            _db = db;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────
        // 1. EMITIR COMPROBANTE
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Emitir(EmitirComprobanteRequest request, string? returnUrl = null)
        {
            if (request.OrdenId <= 0)
            {
                TempData["Error"] = "ID de orden no válido.";
                return RedirigirRetorno(returnUrl, request.OrdenId);
            }

            var (ok, comprobante, error) = await _facturacionService.EmitirComprobanteAsync(request);

            if (!ok)
            {
                TempData["Error"] = error ?? "No se pudo emitir el comprobante electrónico.";
                return RedirigirRetorno(returnUrl, request.OrdenId);
            }

            TempData["Mensaje"] = $"✅ {comprobante!.TipoDocDescripcion} {comprobante.NumeroCompleto} emitida con éxito. Estado SUNAT: {comprobante.EstadoSunat}.";
            return RedirigirRetorno(returnUrl, request.OrdenId);
        }

        // ─────────────────────────────────────────────────────────────
        // 2. DESCARGAR / VER PDF
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> DescargarPdf(int id, bool inline = false)
        {
            var (archivo, contentType, nombreArchivo, error) = await _facturacionService.ObtenerPdfAsync(id);

            if (archivo == null || !string.IsNullOrWhiteSpace(error))
            {
                TempData["Mensaje"] = "Visualizando representación impresa del comprobante.";
                return RedirectToAction("Imprimir", "Comprobante", new { id });
            }

            if (inline)
            {
                Response.Headers["Content-Disposition"] = $"inline; filename=\"{nombreArchivo}\"";
                return File(archivo, contentType);
            }

            return File(archivo, contentType, nombreArchivo);
        }

        // ─────────────────────────────────────────────────────────────
        // 3. DESCARGAR XML
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> DescargarXml(int id)
        {
            var (xml, nombreArchivo, error) = await _facturacionService.ObtenerXmlAsync(id);

            if (string.IsNullOrWhiteSpace(xml) || !string.IsNullOrWhiteSpace(error))
            {
                TempData["Error"] = $"No se pudo obtener el XML: {error}";
                return RedirectToAction("Index", "Orden");
            }

            return File(Encoding.UTF8.GetBytes(xml), "application/xml", nombreArchivo);
        }

        // ─────────────────────────────────────────────────────────────
        // 4. AJAX: DATOS PARA MODAL DE FACTURACIÓN
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ObtenerDatosParaFacturar(int ordenId)
        {
            var orden = await _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Comprobantes)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrdenID == ordenId);

            if (orden == null)
                return NotFound(new { success = false, message = "Orden no encontrada." });

            var comprobanteExistente = orden.Comprobantes?
                .OrderByDescending(c => c.ComprobanteID)
                .FirstOrDefault();

            return Json(new
            {
                success = true,
                ordenId = orden.OrdenID,
                clienteNombre = orden.Cliente?.NombreCompleto ?? "",
                clienteDni = orden.Cliente?.Dni ?? "",
                clienteTelefono = orden.Telefono ?? orden.Cliente?.Telefono ?? "",
                montoTotal = orden.MontoTotal,
                yaFacturado = comprobanteExistente != null && comprobanteExistente.EstadoSunat == "Aceptado",
                comprobante = comprobanteExistente != null ? new
                {
                    id = comprobanteExistente.ComprobanteID,
                    tipo = comprobanteExistente.TipoDocDescripcion,
                    numero = comprobanteExistente.NumeroCompleto,
                    estado = comprobanteExistente.EstadoSunat,
                    fecha = comprobanteExistente.FechaEmision.ToString("dd/MM/yyyy HH:mm")
                } : null
            });
        }

        private IActionResult RedirigirRetorno(string? returnUrl, int ordenId)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Detalles", "Orden", new { id = ordenId });
        }
    }
}
