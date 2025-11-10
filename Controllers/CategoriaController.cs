using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.AspNetCore.Mvc;

namespace AppLavaluc.Controllers
{
    public class CategoriaController : Controller
    {
        private readonly LavanderiaContext _db;

        public CategoriaController(LavanderiaContext db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            IEnumerable<Categoria> lista = _db.Categorias;
            return View(lista);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Categoria categoria)
        {
            if (ModelState.IsValid)
            {
                _db.Categorias.Add(categoria);
                _db.SaveChanges();
                return RedirectToAction("Index");
            }
            return View(categoria);
        }
        // GET: /Categoria/Editar/5
        public IActionResult Editar(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            var categoria = _db.Categorias.Find(id);
            if (categoria == null)
                return NotFound();

            return View(categoria);
        }

        // POST: /Categoria/Editar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Editar(Categoria categoria)
        {
            if (ModelState.IsValid)
            {
                _db.Categorias.Update(categoria);
                _db.SaveChanges();
                return RedirectToAction("Index");
            }
            return View(categoria);
        }
        // GET: /Categoria/Eliminar/5
        public IActionResult Eliminar(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            var categoria = _db.Categorias.Find(id);
            if (categoria == null)
                return NotFound();

            return View(categoria);
        }

        // POST: /Categoria/EliminarConfirmado
        [HttpPost, ActionName("Eliminar")]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarConfirmado(int id)
        {
            var categoria = _db.Categorias.Find(id);
            if (categoria == null)
                return NotFound();

            _db.Categorias.Remove(categoria);
            _db.SaveChanges();
            return RedirectToAction("Index");
        }


    }
}
