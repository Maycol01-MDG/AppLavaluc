using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Services
{
    public class ConsultaDocumentoService : IConsultaDocumentoService
    {
        private readonly HttpClient _httpClient;
        private readonly LavanderiaContext _db;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ConsultaDocumentoService> _logger;

        private const string DefaultLatinfoToken = "lat_2091924312fa68e2c5e9aa8a29886908d57b640cf5e0f9d83c67c3775e2cace2";
        private const string DefaultLatinfoBaseUrl = "https://api.latinfo.dev/pe/kyb";

        public ConsultaDocumentoService(
            HttpClient httpClient,
            LavanderiaContext db,
            IConfiguration configuration,
            ILogger<ConsultaDocumentoService> logger)
        {
            _httpClient = httpClient;
            _db = db;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ConsultaDocumentoRespuesta> ConsultarAsync(string numeroDocumento)
        {
            if (string.IsNullOrWhiteSpace(numeroDocumento))
            {
                return new ConsultaDocumentoRespuesta
                {
                    Exito = false,
                    MensajeError = "Debe ingresar un número de documento."
                };
            }

            var doc = new string(numeroDocumento.Where(char.IsDigit).ToArray());

            if (doc.Length != 8 && doc.Length != 11)
            {
                return new ConsultaDocumentoRespuesta
                {
                    Exito = false,
                    MensajeError = $"Longitud no válida ({doc.Length} dígitos). Ingrese un DNI (8 dígitos) o RUC (11 dígitos)."
                };
            }

            // 1. Buscar en la Base de Datos Local primero (ahorro de cuotas y máxima velocidad)
            var clienteLocal = await _db.Clientes.FirstOrDefaultAsync(c => c.Dni == doc);
            if (clienteLocal != null)
            {
                var tipo = doc.Length == 8 ? TipoDocumento.Dni : TipoDocumento.Ruc;
                return new ConsultaDocumentoRespuesta
                {
                    Exito = true,
                    Tipo = tipo,
                    NumeroDocumento = doc,
                    NombreORazonSocial = clienteLocal.Nombre,
                    Apellidos = clienteLocal.Apellidos,
                    Telefono = clienteLocal.Telefono,
                    Direccion = clienteLocal.Direccion,
                    Origen = "local"
                };
            }

            // 2. Si es DNI (8 dígitos), consultar RENIEC
            if (doc.Length == 8)
            {
                return await ConsultarDniAsync(doc);
            }

            // 3. Si es RUC (11 dígitos), consultar SUNAT vía Latinfo
            return await ConsultarRucAsync(doc);
        }

        private async Task<ConsultaDocumentoRespuesta> ConsultarDniAsync(string dni)
        {
            // Intentar primero con la API Reniec configurada
            var urlTemplate = _configuration["ReniecApi:UrlTemplate"] ?? Environment.GetEnvironmentVariable("RENIEC_API_URL_TEMPLATE");
            var apiKey = _configuration["ReniecApi:ApiKey"] ?? Environment.GetEnvironmentVariable("RENIEC_API_KEY");

            if (!string.IsNullOrWhiteSpace(urlTemplate) && urlTemplate.Contains("{dni}"))
            {
                try
                {
                    var requestUrl = urlTemplate.Replace("{dni}", dni, StringComparison.OrdinalIgnoreCase);
                    using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.Contains("CHANGE_ME"))
                    {
                        request.Headers.Add("x-api-key", apiKey);
                    }

                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var nombreCompleto = doc.RootElement.TryGetProperty("nombreCompleto", out var nc) ? nc.GetString() : null;

                        if (!string.IsNullOrWhiteSpace(nombreCompleto))
                        {
                            var (nombre, apellidos) = SepararNombreApellidos(nombreCompleto);
                            return new ConsultaDocumentoRespuesta
                            {
                                Exito = true,
                                Tipo = TipoDocumento.Dni,
                                NumeroDocumento = dni,
                                NombreORazonSocial = nombre,
                                Apellidos = apellidos,
                                Origen = "reniec"
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error al consultar servicio RENIEC para DNI {Dni}", dni);
                }
            }

            // Fallback: Si existe microservicio local de DNI (por ejemplo puerto 8080)
            try
            {
                var localResp = await _httpClient.GetAsync($"http://localhost:8080/api/dni/{dni}");
                if (localResp.IsSuccessStatusCode)
                {
                    var json = await localResp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var nombreCompleto = doc.RootElement.TryGetProperty("nombreCompleto", out var nc) ? nc.GetString() : "";

                    if (!string.IsNullOrWhiteSpace(nombreCompleto))
                    {
                        var (nombre, apellidos) = SepararNombreApellidos(nombreCompleto);
                        return new ConsultaDocumentoRespuesta
                        {
                            Exito = true,
                            Tipo = TipoDocumento.Dni,
                            NumeroDocumento = dni,
                            NombreORazonSocial = nombre,
                            Apellidos = apellidos,
                            Origen = "reniec_local"
                        };
                    }
                }
            }
            catch
            {
                // Fallback silencioso si el microservicio local no está activo
            }

            return new ConsultaDocumentoRespuesta
            {
                Exito = false,
                Tipo = TipoDocumento.Dni,
                NumeroDocumento = dni,
                MensajeError = "No se encontró información automática para el DNI. Ingrese nombres y apellidos manualmente."
            };
        }

        private async Task<ConsultaDocumentoRespuesta> ConsultarRucAsync(string ruc)
        {
            // 1. Si es RUC 10 (Persona Natural con Negocio), Latinfo KYB no lo sirve por diseño (scope: legal_entities_only).
            //    Por lo tanto, resolvemos directamente con la API SUNAT de Personas Naturales o fallbacks:
            if (ruc.StartsWith("10"))
            {
                // A. Intentar consulta SUNAT pública (apis.net.pe)
                var respApisNet = await ConsultarRucApisNetPeAsync(ruc);
                if (respApisNet.Exito)
                {
                    return respApisNet;
                }

                // B. Intentar consulta en registro local de Greenter
                var respGreenter = await ConsultarRucGreenterAsync(ruc);
                if (respGreenter.Exito)
                {
                    return respGreenter;
                }

                // C. Fallback inteligente: El RUC 10 contiene el DNI en los dígitos 3 al 10 (10{DNI}{DV}).
                //    En SUNAT, la Razón Social de una Persona Natural con RUC 10 es exactamente su Nombre y Apellidos completos.
                var respDni = await ConsultarRuc10PorDniAsync(ruc);
                if (respDni.Exito)
                {
                    return respDni;
                }

                return new ConsultaDocumentoRespuesta
                {
                    Exito = false,
                    Tipo = TipoDocumento.Ruc,
                    NumeroDocumento = ruc,
                    MensajeError = $"RUC 10 detectado. No se encontró información automática en SUNAT. Ingrese nombres/razón social y dirección manualmente."
                };
            }

            // 2. Si es RUC 20 (Persona Jurídica / Empresas) u otros:
            // A. Primero intentar con Latinfo KYB (fuente principal para empresas)
            var respLatinfo = await ConsultarRucLatinfoAsync(ruc);
            if (respLatinfo.Exito)
            {
                return respLatinfo;
            }

            // B. Fallback a apis.net.pe en caso Latinfo falle o no lo tenga
            var respApisNet2 = await ConsultarRucApisNetPeAsync(ruc);
            if (respApisNet2.Exito)
            {
                return respApisNet2;
            }

            // C. Fallback a Greenter
            var respGreenter2 = await ConsultarRucGreenterAsync(ruc);
            if (respGreenter2.Exito)
            {
                return respGreenter2;
            }

            return respLatinfo;
        }

        private async Task<ConsultaDocumentoRespuesta> ConsultarRucApisNetPeAsync(string ruc)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _httpClient.GetAsync($"https://api.apis.net.pe/v1/ruc?numero={ruc}", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("nombre", out var nomProp) && !string.IsNullOrWhiteSpace(nomProp.GetString()))
                    {
                        var razonSocial = nomProp.GetString()!.Trim();
                        if (razonSocial.Equals("RUC invalido", StringComparison.OrdinalIgnoreCase))
                        {
                            return new ConsultaDocumentoRespuesta { Exito = false, Tipo = TipoDocumento.Ruc, NumeroDocumento = ruc, MensajeError = $"El RUC {ruc} no es válido en SUNAT." };
                        }

                        var estado = root.TryGetProperty("estado", out var e) ? e.GetString() : "ACTIVO";
                        var condicion = root.TryGetProperty("condicion", out var c) ? c.GetString() : "HABIDO";

                        var direccion = "";
                        if (root.TryGetProperty("direccion", out var d) && !string.IsNullOrWhiteSpace(d.GetString()) && d.GetString() != "-")
                        {
                            direccion = d.GetString()!.Trim();
                        }

                        var distrito = root.TryGetProperty("distrito", out var dis) ? dis.GetString() : null;
                        var provincia = root.TryGetProperty("provincia", out var pro) ? pro.GetString() : null;
                        var departamento = root.TryGetProperty("departamento", out var dep) ? dep.GetString() : null;

                        var partesUbi = new[] { distrito, provincia, departamento }
                            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "-");
                        var ubiStr = string.Join(" - ", partesUbi);

                        if (!string.IsNullOrWhiteSpace(ubiStr))
                        {
                            direccion = string.IsNullOrWhiteSpace(direccion) ? ubiStr : $"{direccion} ({ubiStr})";
                        }

                        return new ConsultaDocumentoRespuesta
                        {
                            Exito = true,
                            Tipo = TipoDocumento.Ruc,
                            NumeroDocumento = ruc,
                            NombreORazonSocial = razonSocial,
                            Direccion = direccion,
                            Estado = estado,
                            Condicion = condicion,
                            Origen = "sunat"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Consulta apis.net.pe para RUC {Ruc} no disponible: {Message}", ruc, ex.Message);
            }

            return new ConsultaDocumentoRespuesta { Exito = false, Tipo = TipoDocumento.Ruc, NumeroDocumento = ruc };
        }

        private async Task<ConsultaDocumentoRespuesta> ConsultarRucGreenterAsync(string ruc)
        {
            try
            {
                var baseUrl = (_configuration["FacturacionApi:BaseUrl"] ?? "http://greenter.test/api").TrimEnd('/');
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await _httpClient.GetAsync($"{baseUrl}/companies/{ruc}", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("razon_social", out var rz) && !string.IsNullOrWhiteSpace(rz.GetString()))
                    {
                        var dir = root.TryGetProperty("direccion", out var d) ? d.GetString() : "";
                        return new ConsultaDocumentoRespuesta
                        {
                            Exito = true,
                            Tipo = TipoDocumento.Ruc,
                            NumeroDocumento = ruc,
                            NombreORazonSocial = rz.GetString()!.Trim(),
                            Direccion = dir ?? "",
                            Estado = "ACTIVO",
                            Condicion = "HABIDO",
                            Origen = "sunat_greenter"
                        };
                    }
                }
            }
            catch { }

            return new ConsultaDocumentoRespuesta { Exito = false, Tipo = TipoDocumento.Ruc, NumeroDocumento = ruc };
        }

        private async Task<ConsultaDocumentoRespuesta> ConsultarRuc10PorDniAsync(string ruc)
        {
            try
            {
                if (ruc.Length == 11 && ruc.StartsWith("10"))
                {
                    var dni = ruc.Substring(2, 8);
                    var dniResp = await ConsultarDniAsync(dni);
                    if (dniResp.Exito)
                    {
                        var nombreCompleto = $"{dniResp.NombreORazonSocial} {dniResp.Apellidos}".Trim();
                        return new ConsultaDocumentoRespuesta
                        {
                            Exito = true,
                            Tipo = TipoDocumento.Ruc,
                            NumeroDocumento = ruc,
                            NombreORazonSocial = nombreCompleto,
                            Direccion = dniResp.Direccion ?? "",
                            Estado = "ACTIVO",
                            Condicion = "HABIDO",
                            Origen = "sunat_reniec"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error en fallback de RUC 10 por DNI {Ruc}: {Message}", ruc, ex.Message);
            }

            return new ConsultaDocumentoRespuesta { Exito = false, Tipo = TipoDocumento.Ruc, NumeroDocumento = ruc };
        }

        private async Task<ConsultaDocumentoRespuesta> ConsultarRucLatinfoAsync(string ruc)
        {
            var baseUrl = _configuration["LatinfoApi:BaseUrl"] ?? DefaultLatinfoBaseUrl;
            var token = _configuration["LatinfoApi:Token"] ?? DefaultLatinfoToken;

            try
            {
                var requestUri = $"{baseUrl.TrimEnd('/')}/{ruc}";
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _httpClient.SendAsync(request, cts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new ConsultaDocumentoRespuesta
                    {
                        Exito = false,
                        Tipo = TipoDocumento.Ruc,
                        NumeroDocumento = ruc,
                        MensajeError = $"El RUC {ruc} no existe en los registros de SUNAT."
                    };
                }

                var json = await response.Content.ReadAsStringAsync(cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Latinfo respondió con error {Status} para RUC {Ruc}: {Body}", response.StatusCode, ruc, json);
                    return new ConsultaDocumentoRespuesta
                    {
                        Exito = false,
                        Tipo = TipoDocumento.Ruc,
                        NumeroDocumento = ruc,
                        MensajeError = $"No se pudo consultar el RUC con SUNAT (Código HTTP {(int)response.StatusCode}). Ingrese los datos manualmente."
                    };
                }

                var data = JsonSerializer.Deserialize<LatinfoKybResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (data == null || data.Status == "out_of_scope")
                {
                    return new ConsultaDocumentoRespuesta
                    {
                        Exito = false,
                        Tipo = TipoDocumento.Ruc,
                        NumeroDocumento = ruc,
                        MensajeError = "RUC de persona natural no cubierto por el proveedor KYB."
                    };
                }

                var razonSocial = data.Identity?.RazonSocial ?? data.LegalName ?? data.TradeName;
                if (string.IsNullOrWhiteSpace(razonSocial))
                {
                    return new ConsultaDocumentoRespuesta
                    {
                        Exito = false,
                        Tipo = TipoDocumento.Ruc,
                        NumeroDocumento = ruc,
                        MensajeError = "No se pudo obtener la Razón Social para el RUC ingresado."
                    };
                }

                var direccion = FormatearDireccion(data.Identity, data.Activity);
                if (string.IsNullOrWhiteSpace(direccion) && !string.IsNullOrWhiteSpace(data.FiscalAddress))
                {
                    direccion = data.FiscalAddress;
                }

                var estado = data.Identity?.Estado ?? data.Status;
                var condicion = data.Identity?.Condicion;

                return new ConsultaDocumentoRespuesta
                {
                    Exito = true,
                    Tipo = TipoDocumento.Ruc,
                    NumeroDocumento = ruc,
                    NombreORazonSocial = razonSocial.Trim(),
                    Direccion = direccion,
                    Estado = estado,
                    Condicion = condicion,
                    Origen = "sunat_latinfo"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción al consultar RUC {Ruc} vía Latinfo", ruc);
                return new ConsultaDocumentoRespuesta
                {
                    Exito = false,
                    Tipo = TipoDocumento.Ruc,
                    NumeroDocumento = ruc,
                    MensajeError = $"Error al conectar con el servicio SUNAT: {ex.Message}"
                };
            }
        }

        private static string FormatearDireccion(LatinfoIdentity? identity, LatinfoActivity? activity)
        {
            if (identity == null) return string.Empty;

            var partesVia = new List<string>();

            bool EsValido(string? s) => !string.IsNullOrWhiteSpace(s) && s.Trim() != "-" && s.Trim() != "----";

            if (EsValido(identity.TipoVia)) partesVia.Add(identity.TipoVia!.Trim());
            if (EsValido(identity.NombreVia)) partesVia.Add(identity.NombreVia!.Trim());
            if (EsValido(identity.Numero) && identity.Numero!.Trim() != "0") partesVia.Add(identity.Numero!.Trim());
            if (EsValido(identity.Interior)) partesVia.Add($"Int. {identity.Interior!.Trim()}");
            if (EsValido(identity.Manzana)) partesVia.Add($"Mz. {identity.Manzana!.Trim()}");
            if (EsValido(identity.Lote)) partesVia.Add($"Lt. {identity.Lote!.Trim()}");
            if (EsValido(identity.CodigoZona)) partesVia.Add(identity.CodigoZona!.Trim());
            if (EsValido(identity.TipoZona)) partesVia.Add(identity.TipoZona!.Trim());

            var direccionVia = string.Join(" ", partesVia).Trim();

            var partesUbi = new List<string>();
            if (EsValido(activity?.Distrito)) partesUbi.Add(activity!.Distrito!.Trim());
            if (EsValido(activity?.Provincia)) partesUbi.Add(activity!.Provincia!.Trim());
            if (EsValido(activity?.Departamento)) partesUbi.Add(activity!.Departamento!.Trim());

            var ubigeoStr = string.Join(" - ", partesUbi);

            if (!string.IsNullOrWhiteSpace(direccionVia) && !string.IsNullOrWhiteSpace(ubigeoStr))
                return $"{direccionVia}, {ubigeoStr}";

            return !string.IsNullOrWhiteSpace(direccionVia) ? direccionVia : ubigeoStr;
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
