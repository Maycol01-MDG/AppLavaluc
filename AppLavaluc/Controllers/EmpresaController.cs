using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AppLavaluc.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppLavaluc.Controllers
{
    [Authorize]
    public class EmpresaController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<EmpresaController> _logger;

        public EmpresaController(
            IConfiguration config,
            IWebHostEnvironment env,
            IHttpClientFactory httpClientFactory,
            ILogger<EmpresaController> logger)
        {
            _config = config;
            _env = env;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────
        // 1. VISTA DE CONFIGURACIÓN DE EMPRESA Y LOGO
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Index()
        {
            var logoPath = Path.Combine(_env.WebRootPath, "imagenes", "logo_empresa.png");
            var tieneLogo = System.IO.File.Exists(logoPath);

            var model = new EmpresaViewModel
            {
                Ruc = _config["FacturacionApi:Empresa:Ruc"] ?? "10708464100",
                RazonSocial = _config["FacturacionApi:Empresa:RazonSocial"] ?? "MONDRAGON DELGADO MAYCOL",
                NombreComercial = _config["FacturacionApi:Empresa:NombreComercial"] ?? "APP LAVALUC",
                Direccion = _config["FacturacionApi:Empresa:Direccion"] ?? "Av. Villa Nueva 221",
                Ubigeo = _config["FacturacionApi:Empresa:Ubigeo"] ?? "150101",
                Departamento = _config["FacturacionApi:Empresa:Departamento"] ?? "LIMA",
                Provincia = _config["FacturacionApi:Empresa:Provincia"] ?? "LIMA",
                Distrito = _config["FacturacionApi:Empresa:Distrito"] ?? "LIMA",
                Urbanizacion = _config["FacturacionApi:Empresa:Urbanizacion"] ?? "-",
                CodLocal = _config["FacturacionApi:Empresa:CodLocal"] ?? "0000",
                SolUser = "prueba",
                SolPass = "prueba",
                TieneLogo = tieneLogo,
                LogoUrl = tieneLogo ? "/imagenes/logo_empresa.png?v=" + DateTime.UtcNow.Ticks : null
            };

            return View(model);
        }

        // ─────────────────────────────────────────────────────────────
        // 2. GUARDAR DATOS DE EMPRESA Y SUBIR LOGO
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Guardar(EmpresaViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("Index", model);
            }

            try
            {
                // 1. Guardar archivo de Logo en wwwroot/imagenes si se proporcionó uno
                byte[]? logoBytes = null;
                string? logoFileName = null;

                if (model.LogoFile != null && model.LogoFile.Length > 0)
                {
                    var extension = Path.GetExtension(model.LogoFile.FileName).ToLowerInvariant();
                    var allowed = new[] { ".png", ".jpg", ".jpeg", ".webp" };
                    if (!allowed.Contains(extension))
                    {
                        ModelState.AddModelError("LogoFile", "Formato de imagen no permitido. Solo se aceptan PNG, JPG o WEBP.");
                        return View("Index", model);
                    }

                    var imagenesFolder = Path.Combine(_env.WebRootPath, "imagenes");
                    if (!Directory.Exists(imagenesFolder))
                    {
                        Directory.CreateDirectory(imagenesFolder);
                    }

                    var logoDestino = Path.Combine(imagenesFolder, "logo_empresa.png");
                    using (var stream = new FileStream(logoDestino, FileMode.Create))
                    {
                        await model.LogoFile.CopyToAsync(stream);
                    }

                    using var memoryStream = new MemoryStream();
                    await model.LogoFile.CopyToAsync(memoryStream);
                    logoBytes = memoryStream.ToArray();
                    logoFileName = model.LogoFile.FileName;
                }

                // 2. Actualizar archivo appsettings.json local
                await ActualizarAppSettingsAsync(model);

                // 3. Sincronizar con la API de Greenter (POST /api/companies)
                var greenterMsg = await SincronizarConGreenterApiAsync(model, logoBytes, logoFileName);

                TempData["Mensaje"] = $"✅ Datos de empresa y logo actualizados con éxito.{greenterMsg}";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar datos de la empresa o logo.");
                TempData["Error"] = $"Error al guardar configuración: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // ─────────────────────────────────────────────────────────────
        // MÉTODOS PRIVADOS DE APOYO
        // ─────────────────────────────────────────────────────────────
        private async Task ActualizarAppSettingsAsync(EmpresaViewModel model)
        {
            var appsettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
            if (!System.IO.File.Exists(appsettingsPath)) return;

            var jsonText = await System.IO.File.ReadAllTextAsync(appsettingsPath);
            var jsonNode = JsonNode.Parse(jsonText);

            if (jsonNode?["FacturacionApi"]?["Empresa"] is JsonObject empresaObj)
            {
                empresaObj["Ruc"] = model.Ruc.Trim();
                empresaObj["RazonSocial"] = model.RazonSocial.Trim();
                empresaObj["NombreComercial"] = (model.NombreComercial ?? model.RazonSocial).Trim();
                empresaObj["Direccion"] = model.Direccion.Trim();
                empresaObj["Ubigeo"] = (model.Ubigeo ?? "150101").Trim();
                empresaObj["Departamento"] = (model.Departamento ?? "LIMA").Trim();
                empresaObj["Provincia"] = (model.Provincia ?? "LIMA").Trim();
                empresaObj["Distrito"] = (model.Distrito ?? "LIMA").Trim();
                empresaObj["Urbanizacion"] = (model.Urbanizacion ?? "-").Trim();
                empresaObj["CodLocal"] = (model.CodLocal ?? "0000").Trim();

                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                await System.IO.File.WriteAllTextAsync(appsettingsPath, jsonNode.ToJsonString(options), Encoding.UTF8);
            }
        }

        private async Task<string> SincronizarConGreenterApiAsync(EmpresaViewModel model, byte[]? logoBytes, string? logoFileName)
        {
            try
            {
                var baseUrl = (_config["FacturacionApi:BaseUrl"] ?? "http://greenter.test/api").TrimEnd('/');
                var email = _config["FacturacionApi:AuthEmail"] ?? "mondragonmaycol541@gmail.com";
                var password = _config["FacturacionApi:AuthPassword"] ?? "12345678";

                var client = _httpClientFactory.CreateClient();

                // Login para obtener token
                var loginData = new Dictionary<string, string>
                {
                    { "email", email },
                    { "password", password }
                };
                using var formContent = new FormUrlEncodedContent(loginData);
                var loginResp = await client.PostAsync($"{baseUrl}/login", formContent);
                if (!loginResp.IsSuccessStatusCode)
                {
                    return " (Nota: No se pudo autenticar con la API Greenter remota, los cambios se guardaron localmente).";
                }

                var loginJson = await loginResp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(loginJson);
                string? token = null;
                if (doc.RootElement.TryGetProperty("access_token", out var aToken))
                    token = aToken.GetString();
                else if (doc.RootElement.TryGetProperty("token", out var tToken))
                    token = tToken.GetString();

                if (string.IsNullOrWhiteSpace(token))
                {
                    return " (Guardado localmente. Token remoto no disponible).";
                }

                // Enviar multipart/form-data a /companies
                using var multipart = new MultipartFormDataContent();
                multipart.Add(new StringContent(model.RazonSocial), "razon_social");
                multipart.Add(new StringContent(model.Ruc), "ruc");
                multipart.Add(new StringContent(model.Direccion), "direccion");
                multipart.Add(new StringContent(model.SolUser ?? "prueba"), "sol_user");
                multipart.Add(new StringContent(model.SolPass ?? "prueba"), "sol_pass");

                // Certificado de prueba demo requerido por Greenter API
                var certDemo = "-----BEGIN CERTIFICATE-----\nMIIE3zCCA8egAwIBAgIUJ+...DEMO...SUNAT...\n-----END CERTIFICATE-----";
                var certBytes = Encoding.UTF8.GetBytes(certDemo);
                var certContent = new ByteArrayContent(certBytes);
                certContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-pem-file");
                multipart.Add(certContent, "cert", "certificado_demo.pem");

                // Logo file si fue subido
                if (logoBytes != null && logoBytes.Length > 0)
                {
                    var fileContent = new ByteArrayContent(logoBytes);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    multipart.Add(fileContent, "logo", logoFileName ?? "logo_empresa.png");
                }
                else
                {
                    // Si ya existe el logo guardado localmente, lo leemos y enviamos
                    var logoLocal = Path.Combine(_env.WebRootPath, "imagenes", "logo_empresa.png");
                    if (System.IO.File.Exists(logoLocal))
                    {
                        var bytesExistentes = await System.IO.File.ReadAllBytesAsync(logoLocal);
                        var fileContent = new ByteArrayContent(bytesExistentes);
                        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                        multipart.Add(fileContent, "logo", "logo_empresa.png");
                    }
                }

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await client.PostAsync($"{baseUrl}/companies", multipart);
                if (response.IsSuccessStatusCode)
                {
                    return " Sincronizado exitosamente con la API de Greenter.";
                }

                var errText = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Respuesta Greenter /companies ({Status}): {Response}", response.StatusCode, errText);
                return " (El logo y datos se guardaron en el sistema local y se aplicarán a todas las facturas).";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error sincronizando con API Greenter /companies.");
                return " (Guardado en el sistema local).";
            }
        }
    }
}
