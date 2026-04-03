using System.Diagnostics;
using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly LavanderiaContext _context;
        private readonly ILogger<HomeController> _logger;

        public HomeController(LavanderiaContext context, ILogger<HomeController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ✅ CORREGIDO: async + logger real en lugar de catch silencioso
        public async Task<IActionResult> Index()
        {
            try
            {
                ViewBag.TotalClientes = await _context.Clientes.CountAsync();
                ViewBag.TotalOrdenes = await _context.Ordenes.CountAsync();
                ViewBag.TotalServicios = await _context.Servicios.CountAsync();
                ViewBag.TotalCategorias = await _context.Categorias.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estadísticas del dashboard");
                ViewBag.TotalClientes = 0;
                ViewBag.TotalOrdenes = 0;
                ViewBag.TotalServicios = 0;
                ViewBag.TotalCategorias = 0;
                ViewBag.DbError = "No se pudo conectar a la base de datos.";
            }

            return View();
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}