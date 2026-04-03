using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Controllers
{
    [Authorize(Roles = "Admin")]
    public class ReporteController : Controller
    {
        private readonly LavanderiaContext _context;
        private readonly ILogger<ReporteController> _logger;

        public ReporteController(LavanderiaContext context, ILogger<ReporteController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: Reporte/Index
        public async Task<IActionResult> Index(
            string tipo = "Hoy",
            DateTime? fechaInicio = null,
            DateTime? fechaFin = null,
            bool print = false)
        {
            ViewData["IsPrint"] = print;
            var reporte = await ConstruirReporteAsync(tipo, fechaInicio, fechaFin);
            return View(reporte);
        }

        // GET: Reporte/Imprimir
        public async Task<IActionResult> Imprimir(string tipo, DateTime fechaInicio, DateTime fechaFin)
        {
            ViewData["IsPrint"] = true;
            var reporte = await ConstruirReporteAsync(tipo, fechaInicio, fechaFin);
            return View("Index", reporte);
        }

        // ─────────────────────────────────────────────────────────────
        // MÉTODO PRIVADO: Construir reporte
        // ─────────────────────────────────────────────────────────────
        private async Task<Reporte> ConstruirReporteAsync(
            string tipo,
            DateTime? fechaInicio,
            DateTime? fechaFin)
        {
            var (inicio, fin) = ObtenerRangoFechas(tipo, fechaInicio, fechaFin);

            // Órdenes recibidas en el periodo
            var ordenes = await _context.Ordenes
                .Include(o => o.Cliente)
                .Where(o => o.FechaRecepcion >= inicio && o.FechaRecepcion <= fin)
                .OrderByDescending(o => o.OrdenID)
                .AsNoTracking()
                .ToListAsync();

            // Pagos realizados en el periodo
            var pagos = await _context.Pagos
                .Include(p => p.Orden)
                    .ThenInclude(o => o!.Cliente)
                .Where(p => p.FechaPago >= inicio && p.FechaPago <= fin)
                .OrderByDescending(p => p.FechaPago)
                .AsNoTracking()
                .ToListAsync();

            // ✅ CORREGIDO: TotalDeuda ahora es la deuda de las órdenes DEL PERIODO,
            // no de toda la historia. Esto hace el reporte coherente con el filtro aplicado.
            decimal totalDeuda = ordenes.Sum(o => o.SaldoPendiente);

            return new Reporte
            {
                FechaInicio = inicio,
                FechaFin = fin,
                TipoFiltro = tipo,
                Detalles = ordenes,
                PagosDetalle = pagos,
                CantidadOrdenes = ordenes.Count,
                TotalVentas = ordenes.Sum(o => o.MontoTotal),
                TotalRecaudado = pagos.Sum(p => p.Monto),
                TotalDeuda = totalDeuda
            };
        }

        // ✅ CORREGIDO: lógica de fechas extraída a método propio, sin repetición
        private static (DateTime Inicio, DateTime Fin) ObtenerRangoFechas(
            string tipo,
            DateTime? fechaInicio,
            DateTime? fechaFin)
        {
            var hoy = DateTime.Today;

            return tipo switch
            {
                "Hoy" => (hoy, hoy.AddDays(1).AddTicks(-1)),

                "Semana" => (
                    hoy.AddDays(-((int)hoy.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7),
                    hoy.AddDays(-((int)hoy.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7).AddDays(7).AddTicks(-1)
                ),

                "Quincena" => hoy.Day <= 15
                    ? (new DateTime(hoy.Year, hoy.Month, 1),
                       new DateTime(hoy.Year, hoy.Month, 15, 23, 59, 59))
                    : (new DateTime(hoy.Year, hoy.Month, 16),
                       new DateTime(hoy.Year, hoy.Month, DateTime.DaysInMonth(hoy.Year, hoy.Month), 23, 59, 59)),

                "Mes" => (
                    new DateTime(hoy.Year, hoy.Month, 1),
                    new DateTime(hoy.Year, hoy.Month, DateTime.DaysInMonth(hoy.Year, hoy.Month), 23, 59, 59)
                ),

                "Personalizado" when fechaInicio.HasValue && fechaFin.HasValue => (
                    fechaInicio.Value.Date,
                    fechaFin.Value.Date.AddDays(1).AddTicks(-1)
                ),

                // Por defecto: hoy
                _ => (hoy, hoy.AddDays(1).AddTicks(-1))
            };
        }
    }
}