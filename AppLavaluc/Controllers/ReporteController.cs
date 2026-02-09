using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppLavaluc.Data;
using AppLavaluc.Models;

namespace AppLavaluc.Controllers
{
    public class ReporteController : Controller
    {
        private readonly LavanderiaContext _context;

        public ReporteController(LavanderiaContext context)
        {
            _context = context;
        }

        // GET: Reporte/Index
        public async Task<IActionResult> Index(string tipo = "Hoy", DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            // 1. Configurar rango de fechas
            DateTime inicio = DateTime.Today;
            DateTime fin = DateTime.Today.AddDays(1).AddTicks(-1); // Final del día

            switch (tipo)
            {
                case "Hoy":
                    inicio = DateTime.Today;
                    fin = DateTime.Today.AddDays(1).AddTicks(-1);
                    break;
                case "Semana": // Lunes a Domingo actual
                    int diff = (7 + (DateTime.Today.DayOfWeek - DayOfWeek.Monday)) % 7;
                    inicio = DateTime.Today.AddDays(-1 * diff).Date;
                    fin = inicio.AddDays(7).AddTicks(-1);
                    break;
                case "Quincena":
                    if (DateTime.Today.Day <= 15)
                    {
                        inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                        fin = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 15, 23, 59, 59);
                    }
                    else
                    {
                        inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 16);
                        fin = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month), 23, 59, 59);
                    }
                    break;
                case "Mes":
                    inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                    fin = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month), 23, 59, 59);
                    break;
                case "Personalizado":
                    if (fechaInicio.HasValue && fechaFin.HasValue)
                    {
                        inicio = fechaInicio.Value;
                        fin = fechaFin.Value.Date.AddDays(1).AddTicks(-1);
                    }
                    break;
            }

            // 2. Consultar Base de Datos
            var ordenes = await _context.Ordenes
                .Include(o => o.Cliente)
                .Where(o => o.FechaRecepcion >= inicio && o.FechaRecepcion <= fin)
                .OrderByDescending(o => o.OrdenID)
                .AsNoTracking() // Mejora rendimiento para reportes
                .ToListAsync();

            // 3. Construir el ViewModel
            var reporte = new Reporte
            {
                FechaInicio = inicio,
                FechaFin = fin,
                TipoFiltro = tipo,
                Detalles = ordenes,
                CantidadOrdenes = ordenes.Count,
                // Cálculos matemáticos
                TotalVentas = ordenes.Sum(x => x.MontoTotal),
                TotalRecaudado = ordenes.Sum(x => x.MontoPagado),
                TotalDeuda = ordenes.Sum(x => x.SaldoPendiente)
            };

            return View(reporte);
        }

        // GET: Reporte/Imprimir
        // Vista simplificada para impresión (sin menús ni botones)
        public async Task<IActionResult> Imprimir(string tipo, DateTime fechaInicio, DateTime fechaFin)
        {
            // Reutilizamos la lógica, pero retornamos una vista diferente
            return await Index(tipo, fechaInicio, fechaFin);
            // Nota: Lo ideal sería redirigir a una vista "Imprimir.cshtml" específica,
            // pero para simplificar, usaremos la misma lógica.
            // Si quieres una vista dedicada, crea Imprimir.cshtml y cambia el return a View(reporte);
        }
    }
}