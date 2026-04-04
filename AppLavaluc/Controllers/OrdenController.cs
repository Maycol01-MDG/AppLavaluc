using AppLavaluc.Data;
using AppLavaluc.Models;
using AppLavaluc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Controllers
{
    [Authorize]
    public class OrdenController : Controller
    {
        private readonly LavanderiaContext _db;
        private readonly IOrdenService _ordenService;
        private readonly EscPosTicketPrinter _printer;
        private readonly ILogger<OrdenController> _logger;

        public OrdenController(
            LavanderiaContext db,
            IOrdenService ordenService,
            EscPosTicketPrinter printer,
            ILogger<OrdenController> logger)
        {
            _db = db;
            _ordenService = ordenService;
            _printer = printer;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────
        // LISTAR ÓRDENES
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var ordenes = await _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.Servicio)
                .OrderByDescending(o => o.OrdenID)
                .AsNoTracking()
                .ToListAsync();

            return View(ordenes);
        }

        // ─────────────────────────────────────────────────────────────
        // CREAR ORDEN - GET
        // ─────────────────────────────────────────────────────────────
        public IActionResult Crear()
        {
            CargarCategoriasEnViewBag();
            return View();
        }

        // ─────────────────────────────────────────────────────────────
        // SERVICIOS POR CATEGORÍA - AJAX
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<JsonResult> ObtenerServiciosPorCategoria(int categoriaId)
        {
            try
            {
                var servicios = await _db.Servicios
                    .Where(s => s.CategoriaID == categoriaId)
                    .Select(s => new
                    {
                        s.ServicioID,
                        s.NombreServicio,
                        s.Descripcion,
                        s.Precio,
                        s.UnidadMedida
                    })
                    .OrderBy(s => s.NombreServicio)
                    .ToListAsync();

                return Json(new { success = true, data = servicios });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener servicios para categoría {CategoriaId}", categoriaId);
                return Json(new { success = false, message = "Error al obtener los servicios." });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // CREAR ORDEN - POST
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(
            string nombreCliente,
            string apellidosCliente,
            string? telefonoCliente,
            string tipoEntrega,
            decimal montoPagado,
            DateTime? fechaEntregaEstimada,
            string? observaciones,
            [FromForm] string servicioIds,
            [FromForm] string cantidades,
            [FromForm] string descuentos)
        {
            // Validaciones básicas
            if (string.IsNullOrWhiteSpace(nombreCliente) ||
                string.IsNullOrWhiteSpace(apellidosCliente) ||
                string.IsNullOrWhiteSpace(tipoEntrega))
            {
                TempData["Error"] = "Nombre, apellidos y tipo de entrega son obligatorios.";
                CargarCategoriasEnViewBag();
                return RedirectToAction(nameof(Crear));
            }

            var request = new CrearOrdenRequest
            {
                NombreCliente = nombreCliente,
                ApellidosCliente = apellidosCliente,
                TelefonoCliente = telefonoCliente,
                TipoEntrega = tipoEntrega,
                MontoPagado = montoPagado,
                FechaEntregaEstimada = fechaEntregaEstimada,
                Observaciones = observaciones,
                ServicioIds = ParsearIntegers(servicioIds),
                Cantidades = ParsearIntegers(cantidades),
                Descuentos = ParsearDecimales(descuentos)
            };

            var (ok, ordenId, error) = await _ordenService.CrearOrdenAsync(request);

            if (!ok)
            {
                TempData["Error"] = error;
                return RedirectToAction(nameof(Crear));
            }

            await ImprimirYNotificarAsync(ordenId, $"✅ Orden #{ordenId} creada correctamente.");
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // DETALLES
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Detalles(int? id)
        {
            if (id == null) return NotFound();

            var orden = await _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.Servicio)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrdenID == id);

            if (orden == null) return NotFound();

            return View(orden);
        }

        // ─────────────────────────────────────────────────────────────
        // EDITAR - GET
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Editar(int? id)
        {
            if (id == null) return NotFound();

            var orden = await _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.Servicio)
                .FirstOrDefaultAsync(o => o.OrdenID == id);

            if (orden == null) return NotFound();

            return View(orden);
        }

        // ─────────────────────────────────────────────────────────────
        // EDITAR - POST
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(int id, Orden ordenActualizada)
        {
            if (id != ordenActualizada.OrdenID) return NotFound();

            try
            {
                var orden = await _db.Ordenes.FindAsync(id);
                if (orden == null) return NotFound();

                // Solo actualizar campos editables
                orden.Estado = ordenActualizada.Estado;
                orden.FechaEntregaEstimada = ordenActualizada.FechaEntregaEstimada;
                orden.Observaciones = ordenActualizada.Observaciones;
                orden.Telefono = ordenActualizada.Telefono;
                orden.TipoEntrega = ordenActualizada.TipoEntrega;

                await _db.SaveChangesAsync();

                TempData["Mensaje"] = $"✅ Orden #{id} actualizada correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar orden {OrdenId}", id);
                TempData["Error"] = "Error al actualizar la orden.";
                return RedirectToAction(nameof(Editar), new { id });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // ELIMINAR - GET
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Eliminar(int? id)
        {
            if (id == null) return NotFound();

            var orden = await _db.Ordenes
                .Include(o => o.Cliente)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrdenID == id);

            if (orden == null) return NotFound();

            return View(orden);
        }

        // ─────────────────────────────────────────────────────────────
        // ELIMINAR - POST
        // ─────────────────────────────────────────────────────────────
        [HttpPost, ActionName("Eliminar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarConfirmado(int id)
        {
            try
            {
                var orden = await _db.Ordenes
                    .Include(o => o.Detalles)
                    .Include(o => o.Pagos)
                    .FirstOrDefaultAsync(o => o.OrdenID == id);

                if (orden == null) return NotFound();

                // Eliminar registros relacionados antes de la orden
                if (orden.Detalles?.Any() == true)
                    _db.DetallesOrden.RemoveRange(orden.Detalles);

                if (orden.Pagos?.Any() == true)
                    _db.Pagos.RemoveRange(orden.Pagos);

                _db.Ordenes.Remove(orden);
                await _db.SaveChangesAsync();

                TempData["Mensaje"] = $"✅ Orden #{id} eliminada correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar orden {OrdenId}", id);
                TempData["Error"] = "Error al eliminar la orden.";
                return RedirectToAction(nameof(Eliminar), new { id });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // IMPRIMIR TICKET - GET (vista HTML)
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> ImprimirTicket(int? id)
        {
            if (id == null) return NotFound();

            var orden = await _ordenService.ObtenerOrdenConDetallesAsync(id.Value);
            if (orden == null) return NotFound();

            return View(orden);
        }

        // ─────────────────────────────────────────────────────────────
        // ENTREGAR ORDEN - POST
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EntregarOrden(int idOrden)
        {
            var (ok, error) = await _ordenService.EntregarOrdenAsync(idOrden);

            if (!ok)
            {
                TempData["Error"] = error;
                return RedirectToAction(nameof(Index));
            }

            await ImprimirYNotificarAsync(idOrden, $"✅ Orden #{idOrden} entregada y cobrada correctamente.");
            return RedirectToAction(nameof(Index));
        }

       
        private async Task ImprimirYNotificarAsync(int ordenId, string mensajeExito)
        {
            string errorImpresion = string.Empty;
            var orden = await _ordenService.ObtenerOrdenConDetallesAsync(ordenId);

            if (orden != null)
            {
                var impreso = _printer.TryPrintOrder(orden, out errorImpresion);

                if (impreso)
                {
                    TempData["Mensaje"] = mensajeExito;
                    return;
                }
            }
            TempData["Mensaje"] = mensajeExito;
            if (!string.IsNullOrWhiteSpace(errorImpresion))
            {
                TempData["Error"] = $"No se pudo imprimir el ticket. {errorImpresion}".Trim();
            }
        }

        private void CargarCategoriasEnViewBag()
        {
            var categorias = _db.Categorias.OrderBy(c => c.NombreCategoria).ToList();
            ViewBag.Categorias = new SelectList(categorias, "CategoriaID", "NombreCategoria");
        }

        // ✅ CORREGIDO: parsers movidos a métodos estáticos privados
        private static List<int> ParsearIntegers(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return new List<int>();

            return valor
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x.Trim(), out int r) ? r : 0)
                .Where(x => x > 0)
                .ToList();
        }

        private static List<decimal> ParsearDecimales(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return new List<decimal>();

            return valor
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => decimal.TryParse(x.Trim(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal r) ? r : 0)
                .ToList();
        }
    }
}
