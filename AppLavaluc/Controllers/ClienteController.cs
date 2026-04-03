using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Controllers
{
    [Authorize]
    public class ClienteController : Controller
    {
        private readonly LavanderiaContext _db;
        private readonly ILogger<ClienteController> _logger;

        public ClienteController(LavanderiaContext db, ILogger<ClienteController> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var clientes = await _db.Clientes
                .OrderBy(c => c.Nombre)
                .ThenBy(c => c.Apellidos)
                .ToListAsync();

            return View(clientes);
        }

        public IActionResult Crear() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(Cliente cliente)
        {
            if (!ModelState.IsValid) return View(cliente);

            try
            {
                _db.Clientes.Add(cliente);
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = "✅ Cliente creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear cliente");
                TempData["Error"] = "Error al guardar el cliente.";
                return View(cliente);
            }
        }

        public async Task<IActionResult> Editar(int? id)
        {
            if (id == null) return NotFound();
            var cliente = await _db.Clientes.FindAsync(id);
            if (cliente == null) return NotFound();
            return View(cliente);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(Cliente cliente)
        {
            if (!ModelState.IsValid) return View(cliente);

            try
            {
                _db.Clientes.Update(cliente);
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = "✅ Cliente actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al editar cliente {Id}", cliente.ClienteID);
                TempData["Error"] = "Error al actualizar el cliente.";
                return View(cliente);
            }
        }

        public async Task<IActionResult> Detalles(int? id)
        {
            if (id == null) return NotFound();
            var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.ClienteID == id);
            if (cliente == null) return NotFound();
            return View(cliente);
        }

        public async Task<IActionResult> Eliminar(int? id)
        {
            if (id == null) return NotFound();
            var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.ClienteID == id);
            if (cliente == null) return NotFound();
            return View(cliente);
        }

        [HttpPost, ActionName("Eliminar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarConfirmado(int id)
        {
            try
            {
                var cliente = await _db.Clientes.FindAsync(id);
                if (cliente == null) return NotFound();

                _db.Clientes.Remove(cliente);
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = "✅ Cliente eliminado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar cliente {Id}", id);
                TempData["Error"] = "No se pudo eliminar el cliente. Puede tener órdenes asociadas.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}