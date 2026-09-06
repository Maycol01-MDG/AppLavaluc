using System;
using System.Text;
using System.Threading.Tasks;
using AppLavaluc.Data;
using AppLavaluc.Models;
using AppLavaluc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppLavaluc.Controllers
{
    [Authorize]
    public class ComprobanteController : Controller
    {
        private readonly IFacturacionService _facturacionService;
        private readonly LavanderiaContext _db;
        private readonly ILogger<ComprobanteController> _logger;

        public ComprobanteController(
            IFacturacionService facturacionService,
            LavanderiaContext db,
            ILogger<ComprobanteController> logger)
        {
            _facturacionService = facturacionService;
            _db = db;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────
        // 1. LISTADO GENERAL DE COMPROBANTES (PANEL PRINCIPAL)
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index(
            string? tipoDoc = null,
            string? estado = null,
            string? buscar = null,
            DateTime? fechaInicio = null,
            DateTime? fechaFin = null)
        {
            var comprobantes = await _facturacionService.ListarComprobantesAsync(
                tipoDoc, estado, buscar, fechaInicio, fechaFin);

            // Estadísticas resumidas para las tarjetas superiores
            var todos = await _db.Comprobantes.AsNoTracking().ToListAsync();
            ViewBag.TotalComprobantes = todos.Count;
            ViewBag.TotalFacturas = todos.Count(c => c.TipoDoc == "01");
            ViewBag.TotalBoletas = todos.Count(c => c.TipoDoc == "03");
            ViewBag.TotalNotasCredito = todos.Count(c => c.TipoDoc == "07");
            ViewBag.TotalMontoAceptado = todos
                .Where(c => c.EstadoSunat == "Aceptado" && c.TipoDoc != "07")
                .Sum(c => c.MontoTotal);

            ViewBag.FiltroTipoDoc = tipoDoc ?? "TODOS";
            ViewBag.FiltroEstado = estado ?? "TODOS";
            ViewBag.FiltroBuscar = buscar;
            ViewBag.FiltroFechaInicio = fechaInicio?.ToString("yyyy-MM-dd");
            ViewBag.FiltroFechaFin = fechaFin?.ToString("yyyy-MM-dd");

            ViewBag.MotivosNotaCredito = CatalogoSunatNotas.MotivosNotaCredito;

            return View(comprobantes);
        }

        // ─────────────────────────────────────────────────────────────
        // 2. VISUALIZADOR INTERACTIVO Y VISTA PREVIA COMPLETA
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Visualizar(int id, string formato = "oficial", bool autoDownload = false)
        {
            var comprobante = await _facturacionService.ObtenerPorIdAsync(id);
            if (comprobante == null)
            {
                TempData["Error"] = "Comprobante no encontrado.";
                return RedirectToAction(nameof(Index));
            }

            string? htmlContenido = null;
            byte[]? pdfBytes = null;
            var customLogo = ObtenerLogoBase64();

            if (formato == "oficial")
            {
                var (archivo, contentType, _, error) = await _facturacionService.ObtenerPdfAsync(id);
                if (archivo != null && string.IsNullOrWhiteSpace(error))
                {
                    if (archivo.Length >= 4 && archivo[0] == 0x25 && archivo[1] == 0x50 && archivo[2] == 0x44 && archivo[3] == 0x46)
                    {
                        pdfBytes = archivo;
                    }
                    else
                    {
                        var raw = Encoding.UTF8.GetString(archivo);
                        var nombrePdfLocal = $"{comprobante.RucEmisor}-{comprobante.TipoDoc}-{comprobante.Serie}-{comprobante.Correlativo}.pdf";
                        htmlContenido = AjustarHtmlParaA4(raw, nombrePdfLocal, customLogo);
                    }
                }
            }

            ViewBag.Comprobante = comprobante;
            ViewBag.HtmlContenido = htmlContenido;
            ViewBag.PdfBytes = pdfBytes != null ? Convert.ToBase64String(pdfBytes) : null;
            ViewBag.Formato = formato;
            ViewBag.AutoDownload = autoDownload;
            ViewBag.CustomLogoBase64 = customLogo;
            ViewBag.NombrePdf = $"{comprobante.RucEmisor}-{comprobante.TipoDoc}-{comprobante.Serie}-{comprobante.Correlativo}.pdf";
            ViewBag.NombreXml = $"{comprobante.RucEmisor}-{comprobante.TipoDoc}-{comprobante.Serie}-{comprobante.Correlativo}.xml";

            return View(comprobante);
        }

        // ─────────────────────────────────────────────────────────────
        // 3. CONTENIDO HTML LIMPIO PARA IFRAME DEL MODAL DE VISTA PREVIA
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> VisualizarContenido(int id, string formato = "oficial")
        {
            var comprobante = await _facturacionService.ObtenerPorIdAsync(id);
            if (comprobante == null)
            {
                return NotFound("Comprobante no encontrado.");
            }

            var customLogo = ObtenerLogoBase64();
            var nombrePdf = $"{comprobante.RucEmisor}-{comprobante.TipoDoc}-{comprobante.Serie}-{comprobante.Correlativo}.pdf";

            if (formato == "oficial")
            {
                var (archivo, contentType, _, error) = await _facturacionService.ObtenerPdfAsync(id);
                if (archivo != null && string.IsNullOrWhiteSpace(error) && 
                    (contentType.Contains("html", StringComparison.OrdinalIgnoreCase) || !(archivo.Length >= 4 && archivo[0] == 0x25 && archivo[1] == 0x50)))
                {
                    var rawHtml = Encoding.UTF8.GetString(archivo);
                    var htmlAjustado = AjustarHtmlParaA4(rawHtml, nombrePdf, customLogo);
                    return Content(htmlAjustado, "text/html; charset=utf-8");
                }
            }

            // Fallback a plantilla A4 o Ticket
            ViewBag.Formato = formato == "oficial" ? "a4" : formato;
            ViewBag.CustomLogoBase64 = customLogo;
            ViewData["IsPrint"] = true;
            return View("Imprimir", comprobante);
        }

        // ─────────────────────────────────────────────────────────────
        // MÉTODOS PRIVADOS DE OPTIMIZACIÓN A4 Y LOGO
        // ─────────────────────────────────────────────────────────────
        private string? ObtenerLogoBase64()
        {
            try
            {
                var env = HttpContext.RequestServices.GetService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
                if (env == null) return null;
                var logoPath = Path.Combine(env.WebRootPath, "imagenes", "logo_empresa.png");
                if (System.IO.File.Exists(logoPath))
                {
                    var bytes = System.IO.File.ReadAllBytes(logoPath);
                    return "data:image/png;base64," + Convert.ToBase64String(bytes);
                }
            }
            catch { }
            return null;
        }

        private string AjustarHtmlParaA4(string rawHtml, string nombreArchivoPdf, string? customLogoBase64)
        {
            // 1. Reemplazar logo por el logo corporativo actualizado si existe
            if (!string.IsNullOrEmpty(customLogoBase64))
            {
                var logoRegex = new System.Text.RegularExpressions.Regex(@"<img\s+src=""data:image/[^""]+""");
                rawHtml = logoRegex.Replace(
                    rawHtml,
                    $"<img src=\"{customLogoBase64}\" class=\"logo-empresa-header\" style=\"max-height: 160px; max-width: 360px; width: auto; height: auto; object-fit: contain; display: inline-block;\"",
                    1
                );
            }

            // 2. Corregir y eliminar el espacio en blanco del encabezado generado por Greenter
            rawHtml = rawHtml.Replace("padding:30px; !important", "padding: 6px 12px !important");
            rawHtml = rawHtml.Replace("padding: 30px !important", "padding: 6px 12px !important");
            rawHtml = rawHtml.Replace("padding:30px", "padding: 6px 12px");
            rawHtml = rawHtml.Replace("height=\"200px\"", "height=\"auto\"");
            rawHtml = rawHtml.Replace("height=\"90\"", "height=\"auto\"");

            // 3. Inyectar CSS de ajuste A4 para encuadrar en EXACTAMENTE 1 sola hoja
            var cssA4 = @"
<style type=""text/css"">
    @page {
        size: A4 portrait;
        margin: 4mm 6mm;
    }
    html, body {
        background-color: #ffffff !important;
        margin: 0 !important;
        padding: 0 !important;
        font-family: 'Segoe UI', Arial, sans-serif !important;
        font-size: 11px !important;
        height: auto !important;
        overflow: visible !important;
        -webkit-print-color-adjust: exact !important;
        print-color-adjust: exact !important;
    }
    td[style*=""padding:30px""], td[style*=""padding: 30px""] {
        padding: 4px 8px !important;
    }
    table[height=""200px""], table[height=""90""] {
        height: auto !important;
    }
    .table {
        margin-bottom: 5px !important;
        font-size: 10.5px !important;
    }
    .table > tbody > tr > td {
        padding: 3px 5px !important;
        font-size: 10.5px !important;
        line-height: 1.25 !important;
    }
    table td {
        font-size: 10.5px !important;
        padding: 3px 5px !important;
    }
    .tabla_borde {
        border: 1px solid #334155 !important;
        border-radius: 6px !important;
    }
    hr {
        margin-top: 6px !important;
        margin-bottom: 6px !important;
    }
    .logo-empresa-header, table td > span > img, table tr:first-child img {
        max-height: 160px !important;
        max-width: 360px !important;
        height: auto !important;
        width: auto !important;
        object-fit: contain !important;
    }
    /* Estilo de Impresión - Previene hoja 2 en blanco */
    @media print {
        html, body {
            width: 100% !important;
            height: auto !important;
            margin: 0 !important;
            padding: 0 !important;
            overflow: hidden !important;
            page-break-after: avoid !important;
            page-break-inside: avoid !important;
        }
        blockquote, img, tr, table {
            page-break-inside: avoid !important;
        }
    }
</style>
<script src=""https://cdnjs.cloudflare.com/ajax/libs/html2pdf.js/0.10.1/html2pdf.bundle.min.js""></script>
<script>
    window.descargarPdf = function() {
        var opt = {
            margin: [4, 6, 4, 6],
            filename: '" + nombreArchivoPdf + @"',
            image: { type: 'jpeg', quality: 0.98 },
            html2canvas: { scale: 2, useCORS: true, letterRendering: true, scrollY: 0 },
            jsPDF: { unit: 'mm', format: 'a4', orientation: 'portrait' },
            pagebreak: { mode: ['avoid-all', 'css'] }
        };
        return html2pdf().set(opt).from(document.body).save();
    };
</script>
";

            if (rawHtml.Contains("</head>", StringComparison.OrdinalIgnoreCase))
            {
                rawHtml = rawHtml.Replace("</head>", cssA4 + "</head>");
            }
            else if (rawHtml.Contains("</body>", StringComparison.OrdinalIgnoreCase))
            {
                rawHtml = rawHtml.Replace("</body>", cssA4 + "</body>");
            }
            else
            {
                rawHtml += cssA4;
            }

            return rawHtml;
        }

        // ─────────────────────────────────────────────────────────────
        // 4. IMPRESIÓN DIRECTA O DERIVACIÓN A VISUALIZADOR
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Imprimir(int id, string formato = "oficial")
        {
            return RedirectToAction(nameof(Visualizar), new { id, formato });
        }

        // ─────────────────────────────────────────────────────────────
        // 5. DESCARGAR PDF (EVITA ERROR DE CORRUPCIÓN DE ARCHIVO EN CHROME)
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> DescargarPdf(int id)
        {
            var comprobante = await _facturacionService.ObtenerPorIdAsync(id);
            if (comprobante == null)
            {
                TempData["Error"] = "Comprobante no encontrado.";
                return RedirectToAction(nameof(Index));
            }

            var (archivo, contentType, nombreArchivo, error) = await _facturacionService.ObtenerPdfAsync(id);

            // Si es un archivo PDF binario real (%PDF)
            if (archivo != null && string.IsNullOrWhiteSpace(error) &&
                archivo.Length >= 4 && archivo[0] == 0x25 && archivo[1] == 0x50 && archivo[2] == 0x44 && archivo[3] == 0x46)
            {
                return File(archivo, "application/pdf", nombreArchivo);
            }

            // Si la API generó HTML (formato Greenter PHP), redirigimos al visualizador interactivo
            // que realiza la conversión a PDF real mediante html2pdf.js sin fallos en el navegador.
            return RedirectToAction(nameof(Visualizar), new { id, autoDownload = true });
        }

        // ─────────────────────────────────────────────────────────────
        // 4. DESCARGAR XML
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> DescargarXml(int id)
        {
            var (xml, nombreArchivo, error) = await _facturacionService.ObtenerXmlAsync(id);

            if (string.IsNullOrWhiteSpace(xml) || !string.IsNullOrWhiteSpace(error))
            {
                TempData["Error"] = $"No se pudo obtener el XML: {error}";
                return RedirectToAction(nameof(Index));
            }

            return File(Encoding.UTF8.GetBytes(xml), "application/xml", nombreArchivo);
        }

        // ─────────────────────────────────────────────────────────────
        // 5. EMITIR NOTA DE CRÉDITO
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmitirNotaCredito(EmitirNotaCreditoRequest request)
        {
            if (request.ComprobanteId <= 0)
            {
                TempData["Error"] = "Comprobante no válido para emitir Nota de Crédito.";
                return RedirectToAction(nameof(Index));
            }

            var (ok, notaCredito, error) = await _facturacionService.EmitirNotaCreditoAsync(request);

            if (!ok)
            {
                TempData["Error"] = error ?? "No se pudo emitir la Nota de Crédito.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Mensaje"] = $"✅ Nota de Crédito {notaCredito!.NumeroCompleto} emitida con éxito. Estado SUNAT: {notaCredito.EstadoSunat}.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // 6. DETALLES JSON PARA MODAL
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ObtenerDetallesJson(int id)
        {
            var c = await _facturacionService.ObtenerPorIdAsync(id);
            if (c == null)
                return NotFound(new { success = false, message = "Comprobante no encontrado." });

            return Json(new
            {
                success = true,
                id = c.ComprobanteID,
                tipo = c.TipoDocDescripcion,
                serie = c.Serie,
                correlativo = c.Correlativo,
                numero = c.NumeroCompleto,
                fecha = c.FechaEmision.ToString("dd/MM/yyyy HH:mm"),
                emisorRuc = c.RucEmisor,
                emisorRazon = c.RazonSocialEmisor,
                clienteDoc = c.NumDocCliente,
                clienteNombre = c.RznSocialCliente,
                clienteDireccion = c.DireccionCliente,
                total = c.MontoTotal,
                igv = c.MontoIgv,
                gravadas = c.MontoOperacionesGravadas,
                estado = c.EstadoSunat,
                codigo = c.CodigoRespuesta,
                mensaje = c.MensajeSunat,
                hash = c.HashCdr,
                docAfectado = c.NumDocAfectado,
                motivo = c.DesMotivo
            });
        }
    }
}
