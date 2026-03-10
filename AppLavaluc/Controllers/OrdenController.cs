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
                .OrderByDescending(o => o.OrdenID)
                .ToList();

            return View(ordenes);
        }

        // ✅ CREAR ORDEN - GET
        public IActionResult Crear()
        {
            // Cargar categorías para el dropdown
            var categorias = _db.Categorias.OrderBy(c => c.NombreCategoria).ToList();
            ViewBag.Categorias = new SelectList(categorias, "CategoriaID", "NombreCategoria");

            return View();
        }

        // ✅ OBTENER SERVICIOS POR CATEGORÍA - AJAX
        [HttpGet]
        public JsonResult ObtenerServiciosPorCategoria(int categoriaId)
        {
            try
            {
                var servicios = _db.Servicios
                    .Where(s => s.CategoriaID == categoriaId)
                    .Select(s => new
                    {
                        s.ServicioID,
                        s.NombreServicio,
                        s.Descripcion,
                        s.Precio,
                        s.UnidadMedida
                    })
                    .OrderBy(s => s.NombreServicio)
                    .ToList();

                if (servicios.Count == 0)
                    return Json(new { success = true, data = servicios, message = "Sin servicios en esta categoría" });

                return Json(new { success = true, data = servicios });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // ✅ CREAR ORDEN - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Crear(
            string nombreCliente,
            string apellidosCliente,
            string? telefonoCliente,
            string tipoEntrega,
            decimal montoPagado,
            decimal saldoPendiente,
            string estadoPago,

            DateTime? fechaEntregaEstimada,
            string? observaciones,
            [FromForm] string servicioIds,
            [FromForm] string cantidades,
            [FromForm] string descuentos)
        {
            try
            {
                // ============================================
                // 1️⃣ VALIDAR DATOS DEL CLIENTE
                // ============================================
                if (string.IsNullOrWhiteSpace(nombreCliente))
                {
                    TempData["Error"] = "El nombre del cliente es obligatorio.";
                    return RedirectToAction(nameof(Crear));
                }

                if (string.IsNullOrWhiteSpace(apellidosCliente))
                {
                    TempData["Error"] = "Los apellidos del cliente son obligatorios.";
                    return RedirectToAction(nameof(Crear));
                }

                if (string.IsNullOrWhiteSpace(tipoEntrega))
                {
                    TempData["Error"] = "Debe seleccionar un tipo de entrega.";
                    return RedirectToAction(nameof(Crear));
                }

                // ============================================
                // 2️⃣ VALIDAR Y PARSEAR DATOS DE SERVICIOS
                // ============================================
                var servicioIdList = this.ParsearListaIntegers(servicioIds);
                var cantidadList = this.ParsearListaIntegers(cantidades);
                var descuentoList = this.ParsearListaDecimales(descuentos);

                if (servicioIdList.Count == 0)
                {
                    TempData["Error"] = "Debe agregar al menos un servicio a la orden.";
                    return RedirectToAction(nameof(Crear));
                }

                // ============================================
                // 3️⃣ BUSCAR O CREAR CLIENTE
                // ============================================
                var cliente = this.ObtenerOCrearCliente(
                    nombreCliente.Trim(),
                    apellidosCliente.Trim(),
                    telefonoCliente?.Trim());

                // ============================================
                // 4️⃣ CREAR ORDEN
                // ============================================
                var orden = new Orden
                {
                    ClienteID = cliente.ClienteID,
                    FechaRecepcion = DateTime.Now,
                    FechaEntregaEstimada = fechaEntregaEstimada,
                    Estado = "Recibido",
                    TipoEntrega = tipoEntrega,
                    Telefono = telefonoCliente?.Trim(),
                    Observaciones = observaciones?.Trim(),
                    MontoTotal = 0,


                    MontoPagado = montoPagado,
                    SaldoPendiente = saldoPendiente,
                    EstadoPago = estadoPago,
                    EstadoRecojo = "Pendiente",
                };

                _db.Ordenes.Add(orden);
                _db.SaveChanges();

                // ============================================
                // 5️⃣ AGREGAR DETALLES Y CALCULAR TOTAL
                // ============================================
                decimal totalGeneral = this.AgregarDetallesOrden(orden, servicioIdList, cantidadList, descuentoList);

                // ============================================
                // 6️⃣ ACTUALIZAR TOTAL DE LA ORDEN
                // ============================================
                orden.MontoTotal = totalGeneral;
                _db.SaveChanges();

                // ============================================
                // 7️⃣ REGISTRAR EL PAGO INICIAL (SI EXISTE)
                // ============================================
                if (montoPagado > 0)
                {
                    var pagoInicial = new Pago
                    {
                        OrdenID = orden.OrdenID,
                        Monto = montoPagado,
                        FechaPago = DateTime.Now,
                        MetodoPago = "Efectivo", // Por defecto, podrías hacerlo dinámico después
                        Notas = "Pago inicial al crear la orden"
                    };
                    _db.Pagos.Add(pagoInicial);
                    _db.SaveChanges();
                }

                TempData["Mensaje"] = $"✅ Orden #{orden.OrdenID} creada correctamente.";
                TempData["Tipo"] = "success";
                // Esto te envía directo a la vista de impresión con el ID de la orden recién creada
                return RedirectToAction("ImprimirTicket", new { id = orden.OrdenID });
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error al crear la orden: {ex.Message}";
                return RedirectToAction(nameof(Crear));
            }
        }

        // ============================================
        // 🔹 MÉTODO: Obtener o Crear Cliente
        // ============================================
        private Cliente ObtenerOCrearCliente(string nombre, string apellidos, string? telefono)
        {
            var clienteExistente = _db.Clientes.FirstOrDefault(c =>
                c.Nombre == nombre && c.Apellidos == apellidos);

            if (clienteExistente != null)
                return clienteExistente;

            var nuevoCliente = new Cliente
            {
                Nombre = nombre,
                Apellidos = apellidos,
                Telefono = telefono
            };

            _db.Clientes.Add(nuevoCliente);
            _db.SaveChanges();

            return nuevoCliente;
        }

        // ============================================
        // 🔹 MÉTODO: Agregar Detalles a la Orden
        // ============================================
        private decimal AgregarDetallesOrden(
            Orden orden,
            List<int> servicioIds,
            List<int> cantidades,
            List<decimal> descuentos)
        {
            decimal totalGeneral = 0;

            for (int i = 0; i < servicioIds.Count; i++)
            {
                var servicio = _db.Servicios.Find(servicioIds[i]);

                if (servicio == null)
                    continue;

                int cantidad = (i < cantidades.Count && cantidades[i] > 0) ? cantidades[i] : 0;
                decimal descuento = (i < descuentos.Count && descuentos[i] > 0) ? descuentos[i] : 0;

                if (cantidad <= 0)
                    continue;

                // Calcular total del detalle
                decimal total = this.CalcularTotalDetalle(servicio.Precio, cantidad, descuento);

                var detalle = new DetalleOrden
                {
                    OrdenID = orden.OrdenID,
                    ServicioID = servicio.ServicioID,
                    Cantidad = cantidad,
                    PrecioUnitario = servicio.Precio,
                    Descuento = descuento
                };

                _db.DetallesOrden.Add(detalle);
                totalGeneral += total;
            }

            if (_db.ChangeTracker.HasChanges())
                _db.SaveChanges();

            return totalGeneral;
        }

        // ============================================
        // 🔹 MÉTODO: Calcular Total de Detalle
        // ============================================
        private decimal CalcularTotalDetalle(decimal precio, int cantidad, decimal descuento)
        {
            decimal subtotal = precio * cantidad;
            decimal total = subtotal - descuento;
            return Math.Max(0, total);
        }

        // ============================================
        // 🔹 MÉTODO: Parsear Lista de Integers
        // ============================================
        private List<int> ParsearListaIntegers(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return new List<int>();

            return valor.Split(",", System.StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x.Trim(), out int result) ? result : 0)
                .Where(x => x > 0)
                .ToList();
        }

        // ============================================
        // 🔹 MÉTODO: Parsear Lista de Decimales
        // ============================================
        private List<decimal> ParsearListaDecimales(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return new List<decimal>();

            return valor.Split(",", System.StringSplitOptions.RemoveEmptyEntries)
                .Select(x => decimal.TryParse(x.Trim(), out decimal result) ? result : 0)
                .ToList();
        }

        // ✅ VER DETALLES DE ORDEN
        public IActionResult Detalles(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            var orden = _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Detalles)
                .ThenInclude(d => d.Servicio)
                .FirstOrDefault(o => o.OrdenID == id);

            if (orden == null)
                return NotFound();

            return View(orden);
        }

        // ✅ EDITAR ORDEN - GET
        public IActionResult Editar(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            var orden = _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Detalles)
                .FirstOrDefault(o => o.OrdenID == id);

            if (orden == null)
                return NotFound();

            ViewBag.Estados = new SelectList(new[] { "Recibido", "En Proceso", "Listo", "Entregado" }, orden.Estado);
            return View(orden);
        }

        // ✅ EDITAR ORDEN - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Editar(int id, Orden ordenActualizada)
        {
            if (id != ordenActualizada.OrdenID)
                return NotFound();

            try
            {
                var orden = _db.Ordenes.Find(id);
                if (orden == null)
                    return NotFound();

                // Actualizar solo los campos permitidos
                orden.Estado = ordenActualizada.Estado;
                orden.FechaEntregaEstimada = ordenActualizada.FechaEntregaEstimada;
                orden.Observaciones = ordenActualizada.Observaciones;
                orden.Telefono = ordenActualizada.Telefono;

                _db.Ordenes.Update(orden);
                _db.SaveChanges();

                TempData["Mensaje"] = $"✅ Orden #{id} actualizada correctamente.";
                TempData["Tipo"] = "success";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error al actualizar: {ex.Message}";
                return RedirectToAction(nameof(Editar), new { id });
            }
        }

        // ✅ ELIMINAR ORDEN - GET
        public IActionResult Eliminar(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            var orden = _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Detalles)
                .FirstOrDefault(o => o.OrdenID == id);

            if (orden == null)
                return NotFound();

            return View(orden);
        }

        // ✅ ELIMINAR ORDEN - POST
        [HttpPost, ActionName("Eliminar")]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarConfirmado(int id)
        {
            try
            {
                var orden = _db.Ordenes
                    .Include(o => o.Detalles)
                    .FirstOrDefault(o => o.OrdenID == id);

                if (orden == null)
                    return NotFound();

                _db.DetallesOrden.RemoveRange(orden.Detalles ?? new List<DetalleOrden>());
                _db.Ordenes.Remove(orden);
                _db.SaveChanges();

                TempData["Mensaje"] = $"✅ Orden #{id} eliminada correctamente.";
                TempData["Tipo"] = "success";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error al eliminar: {ex.Message}";
                return RedirectToAction(nameof(Eliminar), new { id });
            }
        }

        // ✅ IMPRIMIR TICKET
        public IActionResult ImprimirTicket(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            var orden = _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Detalles)
                .ThenInclude(d => d.Servicio)
                .FirstOrDefault(o => o.OrdenID == id);

            if (orden == null)
                return NotFound();

            return View(orden);
        }


        // ✅ ACCIÓN PARA ENTREGAR Y COBRAR
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EntregarOrden(int idOrden)
        {
            var orden = _db.Ordenes.Find(idOrden);

            if (orden == null)
            {
                return NotFound();
            }

            // Lógica de negocio:
            // 1. Si debía algo, ahora lo paga todo.
            if (orden.SaldoPendiente > 0)
            {
                decimal montoRestante = orden.SaldoPendiente;
                orden.MontoPagado += montoRestante; // Sumamos lo que faltaba
                orden.SaldoPendiente = 0; // La deuda queda en 0

                // REGISTRAR EL PAGO EN LA NUEVA TABLA (EL CORAZÓN DEL REQUERIMIENTO)
                var pagoFinal = new Pago
                {
                    OrdenID = orden.OrdenID,
                    Monto = montoRestante,
                    FechaPago = DateTime.Now,
                    MetodoPago = "Efectivo",
                    Notas = "Pago al momento de recoger la ropa"
                };
                _db.Pagos.Add(pagoFinal);
            }

            // 2. Actualizamos estados
            orden.EstadoPago = "Pagado";
            orden.Estado = "Entregado";

            // 3. (Opcional) Actualizar estado de recojo si usas esa variable
            orden.EstadoRecojo = "Recogido";

            _db.SaveChanges();

            TempData["Mensaje"] = $"✅ Orden #{idOrden} entregada y cobrada correctamente.";
            return RedirectToAction(nameof(Index));
        }





    }
}