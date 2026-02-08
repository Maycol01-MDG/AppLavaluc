using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Controllers
{
    public class ServicioController : Controller
    {
        private readonly LavanderiaContext _db;

        public ServicioController(LavanderiaContext db)
        {
            _db = db;
        }

        // ✅ LISTAR SERVICIOS
        public IActionResult Index()
        {
            // Incluimos la relación con Categoría para que traiga el nombre
            var servicios = _db.Servicios
                .Include(s => s.Categoria) // 👈 agregado
                .ToList();

            return View(servicios);
        }


        // ✅ CREAR SERVICIO (GET)
        public IActionResult Crear()
        {
            ViewBag.Categorias = new SelectList(_db.Categorias, "CategoriaID", "NombreCategoria");
            return View();
        }

        // ✅ CREAR SERVICIO (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Crear(Servicio servicio)
        {
            if (ModelState.IsValid)
            {
                _db.Servicios.Add(servicio);
                _db.SaveChanges();
                return RedirectToAction("Index");
            }

            ViewBag.Categorias = new SelectList(_db.Categorias, "CategoriaID", "NombreCategoria");
            return View(servicio);
        }

        // ✅ EDITAR SERVICIO (GET)
        public IActionResult Editar(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            var servicio = _db.Servicios.Find(id);
            if (servicio == null)
                return NotFound();

            ViewBag.Categorias = new SelectList(_db.Categorias, "CategoriaID", "NombreCategoria", servicio.CategoriaID);
            return View(servicio);
        }

        // ✅ EDITAR SERVICIO (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Editar(Servicio servicio)
        {
            if (ModelState.IsValid)
            {
                _db.Servicios.Update(servicio);
                _db.SaveChanges();
                return RedirectToAction("Index");
            }

            ViewBag.Categorias = new SelectList(_db.Categorias, "CategoriaID", "NombreCategoria", servicio.CategoriaID);
            return View(servicio);
        }

        // ✅ ELIMINAR SERVICIO (GET)
        public IActionResult Eliminar(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            var servicio = _db.Servicios.Find(id);
            if (servicio == null)
                return NotFound();

            return View(servicio);
        }

        // ✅ ELIMINAR SERVICIO (POST)
        [HttpPost, ActionName("Eliminar")]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarConfirmado(int id)
        {
            var servicio = _db.Servicios.Find(id);
            if (servicio == null)
                return NotFound();

            _db.Servicios.Remove(servicio);
            _db.SaveChanges();
            return RedirectToAction("Index");
        }
    }
}
