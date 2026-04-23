using AppLavaluc.Data;
using AppLavaluc.Models;
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
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public ClienteController(
            LavanderiaContext db,
            ILogger<ClienteController> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _db = db;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
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
        public async Task<IActionResult> BuscarDni(string dni)
        {
            if (string.IsNullOrWhiteSpace(dni) || dni.Length != 8 || !dni.All(char.IsDigit))
                return BadRequest(new { mensaje = "El DNI debe tener 8 dígitos numéricos." });

            var urlTemplate = _configuration["ReniecApi:UrlTemplate"] ?? Environment.GetEnvironmentVariable("RENIEC_API_URL_TEMPLATE");
            var apiKey = _configuration["ReniecApi:ApiKey"] ?? Environment.GetEnvironmentVariable("RENIEC_API_KEY");

            if (string.IsNullOrWhiteSpace(urlTemplate) || !urlTemplate.Contains("{dni}", StringComparison.OrdinalIgnoreCase))
                return StatusCode(500, new { mensaje = "Falta configurar ReniecApi:UrlTemplate con el marcador {dni}." });

            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
                return StatusCode(500, new { mensaje = "Falta configurar la API Key de RENIEC en el servidor." });

            try
            {
                var requestUrl = urlTemplate.Replace("{dni}", dni, StringComparison.OrdinalIgnoreCase);
                var client = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Add("x-api-key", apiKey);

                using var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var detalle = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("RENIEC respondió {StatusCode} para DNI {Dni}", response.StatusCode, dni);
                    var mensaje = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
                        ? $"RENIEC respondió {(int)response.StatusCode}. {detalle}"
                        : "No se pudo consultar RENIEC en este momento.";
                    return StatusCode((int)response.StatusCode, new { mensaje });
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var nombreCompleto = root.TryGetProperty("nombreCompleto", out var nc)
                    ? nc.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(nombreCompleto))
                    return NotFound(new { mensaje = "No se encontró información para ese DNI." });

                var (nombre, apellidos) = SepararNombreApellidos(nombreCompleto);
                return Json(new
                {
                    dni,
                    nombre,
                    apellidos,
                    nombreCompleto
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar RENIEC para DNI {Dni}", dni);
                return StatusCode(500, new { mensaje = "Error interno al consultar DNI." });
            }
        }

        private static (string nombre, string apellidos) SepararNombreApellidos(string nombreCompleto)
        {
            var partes = nombreCompleto
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (partes.Length >= 3)
            {
                var apellidos = string.Join(' ', partes.TakeLast(2));
                var nombre = string.Join(' ', partes.Take(partes.Length - 2));
                return (nombre, apellidos);
            }

            if (partes.Length == 2)
                return (partes[0], partes[1]);

            return (nombreCompleto, string.Empty);
        }
    }
}
