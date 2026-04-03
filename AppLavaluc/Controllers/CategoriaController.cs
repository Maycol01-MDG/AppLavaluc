using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Controllers
{
    [Authorize]
    public class CategoriaController : Controller
    {
        private readonly LavanderiaContext _db;
        private readonly ILogger<CategoriaController> _logger;

        public CategoriaController(LavanderiaContext db, ILogger<CategoriaController> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var lista = await _db.Categorias.OrderBy(c => c.NombreCategoria).ToListAsync();
            return View(lista);
        }

        public IActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Categoria categoria)
        {
            if (!ModelState.IsValid) return View(categoria);

            try
            {
                _db.Categorias.Add(categoria);
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = "✅ Categoría creada correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear categoría");
                TempData["Error"] = "Error al guardar la categoría.";
                return View(categoria);
            }
        }

        public async Task<IActionResult> Editar(int? id)
        {
            if (id == null) return NotFound();
            var categoria = await _db.Categorias.FindAsync(id);
            if (categoria == null) return NotFound();
            return View(categoria);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(Categoria categoria)
        {
            if (!ModelState.IsValid) return View(categoria);

            try
            {
                _db.Categorias.Update(categoria);
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = "✅ Categoría actualizada correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al editar categoría {Id}", categoria.CategoriaID);
                TempData["Error"] = "Error al actualizar la categoría.";
                return View(categoria);
            }
        }

        public async Task<IActionResult> Eliminar(int? id)
        {
            if (id == null) return NotFound();
            var categoria = await _db.Categorias.FindAsync(id);
            if (categoria == null) return NotFound();
            return View(categoria);
        }

        [HttpPost, ActionName("Eliminar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarConfirmado(int id)
        {
            try
            {
                var categoria = await _db.Categorias.FindAsync(id);
                if (categoria == null) return NotFound();

                _db.Categorias.Remove(categoria);
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = "✅ Categoría eliminada correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar categoría {Id}", id);
                TempData["Error"] = "No se pudo eliminar la categoría. Puede tener servicios asociados.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}