using AppLavaluc.Data;
using AppLavaluc.Models;
using AppLavaluc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AppLavaluc.Controllers
{
    [Authorize]
    public class ClienteController : Controller
    {
        private readonly LavanderiaContext _db;
        private readonly ILogger<ClienteController> _logger;
        private readonly IConsultaDocumentoService _consultaDocumentoService;

        public ClienteController(
            LavanderiaContext db,
            ILogger<ClienteController> logger,
            IConsultaDocumentoService consultaDocumentoService)
        {
            _db = db;
            _logger = logger;
            _consultaDocumentoService = consultaDocumentoService;
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

        [HttpGet]
        public async Task<IActionResult> BuscarDocumento(string documento)
        {
            if (string.IsNullOrWhiteSpace(documento))
                return BadRequest(new { mensaje = "Ingrese un número de documento." });

            var doc = new string(documento.Where(char.IsDigit).ToArray());
            if (doc.Length != 8 && doc.Length != 11)
                return BadRequest(new { mensaje = "El documento debe ser un DNI de 8 dígitos o un RUC de 11 dígitos." });

            var resp = await _consultaDocumentoService.ConsultarAsync(doc);

            if (resp.Exito)
            {
                var tipoDoc = resp.Tipo == TipoDocumento.Ruc ? "01" : "03";
                var tipoDocSunat = resp.Tipo == TipoDocumento.Ruc ? "6" : "1";

                string mensajeExito;
                if (resp.Origen == "local")
                {
                    mensajeExito = "Cliente frecuente cargado desde la base de datos local.";
                }
                else if (resp.Tipo == TipoDocumento.Ruc)
                {
                    var estadoTxt = !string.IsNullOrWhiteSpace(resp.Estado) ? $" ({resp.Estado} / {resp.Condicion ?? "HABIDO"})" : "";
                    mensajeExito = $"✓ RUC verificado en SUNAT: {resp.NombreORazonSocial}{estadoTxt}";
                }
                else
                {
                    mensajeExito = "Datos obtenidos desde RENIEC.";
                }

                return Json(new
                {
                    encontrado = true,
                    origen = resp.Origen,
                    tipoDoc = tipoDoc,
                    tipoDocSunat = tipoDocSunat,
                    documento = doc,
                    nombre = resp.NombreORazonSocial,
                    apellidos = resp.Apellidos ?? "",
                    nombreCompleto = !string.IsNullOrWhiteSpace(resp.Apellidos)
                        ? $"{resp.NombreORazonSocial} {resp.Apellidos}".Trim()
                        : resp.NombreORazonSocial,
                    telefono = resp.Telefono ?? "",
                    direccion = resp.Direccion ?? "",
                    estado = resp.Estado ?? "",
                    condicion = resp.Condicion ?? "",
                    mensaje = mensajeExito
                });
            }

            var fallbackTipoDoc = doc.Length == 11 ? "01" : "03";
            var fallbackTipoDocSunat = doc.Length == 11 ? "6" : "1";

            return Json(new
            {
                encontrado = false,
                origen = "nuevo",
                tipoDoc = fallbackTipoDoc,
                tipoDocSunat = fallbackTipoDocSunat,
                documento = doc,
                mensaje = resp.MensajeError ?? (doc.Length == 11
                    ? "RUC detectado. Ingrese la Razón Social y Dirección Fiscal para emitir Factura."
                    : "DNI no registrado localmente. Ingrese nombres y apellidos para emitir Boleta.")
            });
        }

        [HttpGet]
        public async Task<IActionResult> BuscarDni(string dni)
        {
            if (string.IsNullOrWhiteSpace(dni) || dni.Length != 8 || !dni.All(char.IsDigit))
                return BadRequest(new { mensaje = "El DNI debe tener 8 dígitos numéricos." });

            var resp = await _consultaDocumentoService.ConsultarAsync(dni);
            if (!resp.Exito)
                return NotFound(new { mensaje = resp.MensajeError ?? "No se encontró información para el DNI." });

            return Json(new
            {
                dni,
                nombre = resp.NombreORazonSocial,
                apellidos = resp.Apellidos ?? "",
                nombreCompleto = !string.IsNullOrWhiteSpace(resp.Apellidos)
                    ? $"{resp.NombreORazonSocial} {resp.Apellidos}".Trim()
                    : resp.NombreORazonSocial,
                telefono = resp.Telefono ?? "",
                direccion = resp.Direccion ?? ""
            });
        }

    }
}
