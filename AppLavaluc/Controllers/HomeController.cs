using System.Diagnostics;
using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace AppLavaluc.Controllers
{
    [Authorize] // <-- PROTEGIDO
    public class HomeController : Controller
    {
        private readonly LavanderiaContext _context;

        public HomeController(LavanderiaContext context)
        {
            _context = context;
        }


        public IActionResult Index()
        {
            ViewBag.TotalClientes = _context.Clientes.Count();
            ViewBag.TotalOrdenes = _context.Ordenes.Count();
            ViewBag.TotalServicios = _context.Servicios.Count();
            ViewBag.TotalCategorias = _context.Categorias.Count();

            return View();
        }


        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }

}
