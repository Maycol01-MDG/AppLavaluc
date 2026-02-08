using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Controllers
{
    public class OrdenController : Controller
    {
        private readonly LavanderiaContext _db;

        public OrdenController(LavanderiaContext db)
        {
            _db = db;
        }

        // ✅ LISTAR ÓRDENES
        public IActionResult Index()
        {
            var ordenes = _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Detalles)
                .ThenInclude(d => d.Servicio)
                .ToList();

            return View(ordenes);
        }

        // ✅ MÉTODO CREAR GET - CORREGIDO
        public IActionResult Crear()
        {
            ViewBag.Categorias = new SelectList(_db.Categorias, "CategoriaID", "NombreCategoria");
            ViewBag.Clientes = new SelectList(_db.Clientes, "ClienteID", "NombreCompleto");

            // 🔹 AGREGAR Descripcion al Select
            ViewBag.Servicios = _db.Servicios
                .Select(s => new
                {
                    s.ServicioID,
                    s.NombreServicio,
                    s.Precio,
                    s.Descripcion  // ✅ AGREGADO
                })
                .ToList();

            return View();
        }


        // ✅ AJAX: Obtener servicios por categoría
        [HttpGet]
        public JsonResult ObtenerServiciosPorCategoria(int categoriaId)
        {
            var servicios = _db.Servicios
                .Where(s => s.CategoriaID == categoriaId)
                .Select(s => new
                {
                    s.ServicioID,
                    s.NombreServicio,
                    s.Precio
                })
                .ToList();

            return Json(servicios);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Crear(Orden orden, string nombreCliente, string? apellidosCliente,
            string? telefonoCliente, List<int>? servicioIds, List<int>? cantidades, List<decimal>? descuentos)
        {
            // ✅ Validar inputs
            if (string.IsNullOrWhiteSpace(nombreCliente))
            {
                ModelState.AddModelError("nombreCliente", "Debe ingresar el nombre del cliente.");
            }

            if (servicioIds == null || servicioIds.Count == 0)
            {
                ModelState.AddModelError("servicios", "Debe agregar al menos un servicio a la orden.");
            }

            if (!ModelState.IsValid)
            {
                // 🔹 Recargar ViewBag si falla
                ViewBag.Categorias = new SelectList(_db.Categorias, "CategoriaID", "NombreCategoria");
                ViewBag.Clientes = new SelectList(_db.Clientes, "ClienteID", "NombreCompleto");
                ViewBag.Servicios = _db.Servicios
                    .Select(s => new { s.ServicioID, s.NombreServicio, s.Precio, s.Descripcion })
                    .ToList();
                return View(orden);
            }

            // ✅ Buscar o crear cliente
            var clienteExistente = _db.Clientes.FirstOrDefault(c =>
                c.Nombre == nombreCliente &&
                (string.IsNullOrEmpty(apellidosCliente) ? string.IsNullOrEmpty(c.Apellidos) : c.Apellidos == apellidosCliente)
            );

            if (clienteExistente == null)
            {
                clienteExistente = new Cliente
                {
                    Nombre = nombreCliente,
                    Apellidos = apellidosCliente,
                    Telefono = telefonoCliente
                };
                _db.Clientes.Add(clienteExistente);
                _db.SaveChanges();
            }

            // ✅ Crear orden
            orden.ClienteID = clienteExistente.ClienteID;
            orden.FechaRecepcion = DateTime.Now;
            orden.Estado = "Recibido";
            orden.MontoTotal = 0;

            _db.Ordenes.Add(orden);
            _db.SaveChanges();

            // ✅ Agregar detalles
            decimal totalGeneral = 0;
            for (int i = 0; i < servicioIds.Count; i++)
            {
                var servicio = _db.Servicios.Find(servicioIds[i]);
                if (servicio != null)
                {
                    var cantidad = cantidades?[i] ?? 0;
                    var descuento = descuentos?[i] ?? 0;

                    if (cantidad <= 0) continue;

                    var total = Math.Max(0, (servicio.Precio * cantidad) - descuento);

                    var detalle = new DetalleOrden
                    {
                        OrdenID = orden.OrdenID,
                        ServicioID = servicio.ServicioID,
                        Cantidad = cantidad,
                        Descuento = descuento,
                        PrecioUnitario = servicio.Precio
                    };

                    _db.DetallesOrden.Add(detalle);
                    totalGeneral += total;
                }
            }

            orden.MontoTotal = totalGeneral;
            _db.SaveChanges();

            TempData["Mensaje"] = "✅ Orden creada correctamente.";
            return RedirectToAction(nameof(Index));
        }


        // ✅ AGREGAR MÉTODOS FALTANTES
        public IActionResult Editar(int? id)
        {
            if (id == null || id == 0) return NotFound();

            var orden = _db.Ordenes.Find(id);
            if (orden == null) return NotFound();

            ViewBag.Clientes = new SelectList(_db.Clientes, "ClienteID", "NombreCompleto", orden.ClienteID);
            return View(orden);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Editar(Orden orden)
        {
            if (ModelState.IsValid)
            {
                _db.Ordenes.Update(orden);
                _db.SaveChanges();
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Clientes = new SelectList(_db.Clientes, "ClienteID", "NombreCompleto", orden.ClienteID);
            return View(orden);
        }

        public IActionResult Eliminar(int? id)
        {
            if (id == null || id == 0) return NotFound();

            var orden = _db.Ordenes
                .Include(o => o.Cliente)
                .FirstOrDefault(o => o.OrdenID == id);

            if (orden == null) return NotFound();

            return View(orden);
        }

        [HttpPost, ActionName("Eliminar")]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarConfirmado(int id)
        {
            var orden = _db.Ordenes.Find(id);
            if (orden == null) return NotFound();

            _db.Ordenes.Remove(orden);
            _db.SaveChanges();

            TempData["Mensaje"] = "✅ Orden eliminada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        // ✅ DETALLES / IMPRIMIR TICKET
        public IActionResult ImprimirTicket(int id)
        {
            var orden = _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Detalles)
                .ThenInclude(d => d.Servicio)
                .FirstOrDefault(o => o.OrdenID == id);

            if (orden == null)
                return NotFound();

            return View(orden);
        }

        // ✅ DETALLES DE UNA ORDEN
        public IActionResult Detalles(int id)
        {
            var orden = _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Detalles)
                .ThenInclude(d => d.Servicio)
                .FirstOrDefault(o => o.OrdenID == id);

            if (orden == null)
                return NotFound();

            return View(orden);
        }
    }
}
