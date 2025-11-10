using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.AspNetCore.Mvc;

namespace AppLavaluc.Controllers
{
    public class ClienteController : Controller
    {
        private readonly LavanderiaContext _db;

        public ClienteController(LavanderiaContext db)
        {
            _db = db;
        }

        // ✅ LISTAR CLIENTES
        public IActionResult Index()
        {
            var clientes = _db.Clientes.ToList();
            return View(clientes);
        }

        // ✅ CREAR CLIENTE (GET)
        public IActionResult Crear()
        {
            return View();
        }

        // ✅ CREAR CLIENTE (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Crear(Cliente cliente)
        {
            if (ModelState.IsValid)
            {
                _db.Clientes.Add(cliente);
                _db.SaveChanges();
                return RedirectToAction(nameof(Index));
            }
            return View(cliente);
        }

        // ✅ EDITAR CLIENTE (GET)
        public IActionResult Editar(int? id)
        {
            if (id == null) return NotFound();
            var cliente = _db.Clientes.Find(id);
            if (cliente == null) return NotFound();

            return View(cliente);
        }

        // ✅ EDITAR CLIENTE (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Editar(Cliente cliente)
        {
            if (ModelState.IsValid)
            {
                _db.Clientes.Update(cliente);
                _db.SaveChanges();
                return RedirectToAction(nameof(Index));
            }
            return View(cliente);
        }

        // ✅ DETALLES CLIENTE
        public IActionResult Detalles(int? id)
        {
            if (id == null) return NotFound();
            var cliente = _db.Clientes.FirstOrDefault(c => c.ClienteID == id);
            if (cliente == null) return NotFound();

            return View(cliente);
        }

        // ✅ ELIMINAR CLIENTE (GET)
        public IActionResult Eliminar(int? id)
        {
            if (id == null) return NotFound();
            var cliente = _db.Clientes.FirstOrDefault(c => c.ClienteID == id);
            if (cliente == null) return NotFound();

            return View(cliente);
        }

        // ✅ ELIMINAR CLIENTE (POST)
        [HttpPost, ActionName("Eliminar")]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarConfirmado(int id)
        {
            var cliente = _db.Clientes.Find(id);
            if (cliente == null) return NotFound();

            _db.Clientes.Remove(cliente);
            _db.SaveChanges();
            return RedirectToAction(nameof(Index));
        }
    }
}
