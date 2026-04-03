using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Controllers
{
    [Authorize]
    public class ServicioController : Controller
    {
        private readonly LavanderiaContext _db;
        private readonly ILogger<ServicioController> _logger;

        public ServicioController(LavanderiaContext db, ILogger<ServicioController> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var servicios = await _db.Servicios
                .Include(s => s.Categoria)
                .OrderBy(s => s.Categoria!.NombreCategoria)
                .ThenBy(s => s.NombreServicio)
                .ToListAsync();

            return View(servicios);
        }

        public async Task<IActionResult> Crear()
        {
            await CargarCategoriasEnViewBagAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(Servicio servicio)
        {
            if (!ModelState.IsValid)
            {
                await CargarCategoriasEnViewBagAsync(servicio.CategoriaID);
                return View(servicio);
            }

            try
            {
                _db.Servicios.Add(servicio);
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = "✅ Servicio creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear servicio");
                TempData["Error"] = "Error al guardar el servicio.";
                await CargarCategoriasEnViewBagAsync(servicio.CategoriaID);
                return View(servicio);
            }
        }

        public async Task<IActionResult> Editar(int? id)
        {
            if (id == null) return NotFound();
            var servicio = await _db.Servicios.FindAsync(id);
            if (servicio == null) return NotFound();
            await CargarCategoriasEnViewBagAsync(servicio.CategoriaID);
            return View(servicio);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(Servicio servicio)
        {
            if (!ModelState.IsValid)
            {
                await CargarCategoriasEnViewBagAsync(servicio.CategoriaID);
                return View(servicio);
            }

            try
            {
                _db.Servicios.Update(servicio);
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = "✅ Servicio actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al editar servicio {Id}", servicio.ServicioID);
                TempData["Error"] = "Error al actualizar el servicio.";
                await CargarCategoriasEnViewBagAsync(servicio.CategoriaID);
                return View(servicio);
            }
        }

        public async Task<IActionResult> Eliminar(int? id)
        {
            if (id == null) return NotFound();
            var servicio = await _db.Servicios
                .Include(s => s.Categoria)
                .FirstOrDefaultAsync(s => s.ServicioID == id);
            if (servicio == null) return NotFound();
            return View(servicio);
        }

        [HttpPost, ActionName("Eliminar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarConfirmado(int id)
        {
            try
            {
                var servicio = await _db.Servicios.FindAsync(id);
                if (servicio == null) return NotFound();

                _db.Servicios.Remove(servicio);
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = "✅ Servicio eliminado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar servicio {Id}", id);
                TempData["Error"] = "No se pudo eliminar el servicio. Puede estar asociado a órdenes existentes.";
                return RedirectToAction(nameof(Index));
            }
        }

        private async Task CargarCategoriasEnViewBagAsync(int? selectedId = null)
        {
            var categorias = await _db.Categorias
                .OrderBy(c => c.NombreCategoria)
                .ToListAsync();

            ViewBag.Categorias = new SelectList(categorias, "CategoriaID", "NombreCategoria", selectedId);
        }
    }
}