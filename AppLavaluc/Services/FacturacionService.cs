using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppLavaluc.Services
{
    public class FacturacionService : IFacturacionService
    {
        private readonly LavanderiaContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<FacturacionService> _logger;

        private static string? _cachedToken;
        private static DateTime _tokenExpiresAt = DateTime.MinValue;
        private static readonly object _tokenLock = new();

        public FacturacionService(
            LavanderiaContext db,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<FacturacionService> logger)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────
        // 1. EMITIR COMPROBANTE (FACTURA O BOLETA)
        // ─────────────────────────────────────────────────────────────
        public async Task<(bool Ok, Comprobante? Comprobante, string? Error)> EmitirComprobanteAsync(EmitirComprobanteRequest req)
        {
            try
            {
                var orden = await _db.Ordenes
                    .Include(o => o.Cliente)
                    .Include(o => o.Detalles!)
                        .ThenInclude(d => d.Servicio)
                    .Include(o => o.Comprobantes)
                    .FirstOrDefaultAsync(o => o.OrdenID == req.OrdenId);

                if (orden == null)
                    return (false, null, "Orden no encontrada.");

                if (orden.Comprobantes != null && orden.Comprobantes.Any(c => c.EstadoSunat == "Aceptado" && c.TipoDoc != "07"))
                {
                    var existente = orden.Comprobantes.First(c => c.EstadoSunat == "Aceptado" && c.TipoDoc != "07");
                    return (false, existente, $"Esta orden ya cuenta con el comprobante {existente.NumeroCompleto} aceptado por SUNAT.");
                }

                if (orden.Detalles == null || !orden.Detalles.Any())
                    return (false, null, "La orden no contiene detalles para facturar.");

                var tipoDoc = string.IsNullOrWhiteSpace(req.TipoDoc) ? "03" : req.TipoDoc.Trim();
                var serie = string.IsNullOrWhiteSpace(req.Serie)
                    ? (tipoDoc == "01"
                        ? (_config["FacturacionApi:SerieFactura"] ?? "F001")
                        : (_config["FacturacionApi:SerieBoleta"] ?? "B001"))
                    : req.Serie.Trim().ToUpper();

                var correlativo = await ObtenerSiguienteCorrelativoAsync(tipoDoc, serie);
                var fechaEmision = DateTime.Now;

                // Datos de la Empresa Emisora
                var company = ObtenerDatosEmpresa();

                // Datos del Cliente Receptor
                var client = ConstruirClienteReceptor(req, orden.Cliente, tipoDoc);

                // Desglose de Servicios (Cálculo IGV 18% UBL 2.1)
                var (details, totalGravadas, totalIgv, totalGeneral) = ConstruirDetallesUbl(orden.Detalles);

                var invoiceRequest = new InvoiceRequest
                {
                    UblVersion = "2.1",
                    TipoDoc = tipoDoc,
                    TipoOperacion = "0101",
                    Serie = serie,
                    Correlativo = correlativo.ToString(CultureInfo.InvariantCulture),
                    FechaEmision = fechaEmision.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                    FormaPago = new FormaPagoDto { Moneda = "PEN", Tipo = "Contado" },
                    TipoMoneda = "PEN",
                    Company = company,
                    Client = client,
                    Details = details
                };

                // Enviar a la API de Facturación si está disponible, o registrar en modo contingencia
                var token = await ObtenerTokenAsync();
                bool exitoApi = false;
                string estadoSunat = "Pendiente";
                string? codigoRespuesta = "0000";
                string? mensajeSunat = "Registrado localmente (Pendiente de sincronización SUNAT)";
                string? hashCdr = null;
                string? nombreArchivo = $"{company.Ruc}-{tipoDoc}-{serie}-{correlativo}";

                if (!string.IsNullOrWhiteSpace(token))
                {
                    try
                    {
                        var res = await EnviarFacturaApiAsync(invoiceRequest, token);
                        exitoApi = res.Exito;
                        estadoSunat = res.Estado;
                        codigoRespuesta = res.Codigo;
                        mensajeSunat = res.Mensaje;
                        hashCdr = res.Hash;
                        nombreArchivo = res.NombreArchivo;
                    }
                    catch (Exception exApi)
                    {
                        _logger.LogWarning(exApi, "Fallo al comunicar con la API de Facturación para {Serie}-{Correlativo}. Se registra como Pendiente.", serie, correlativo);
                    }
                }
                else
                {
                    _logger.LogWarning("Sin conexión a la API de Facturación externa. Se registra comprobante {Serie}-{Correlativo} en modo local (Pendiente).", serie, correlativo);
                }

                // Guardar en la base de datos
                var comprobante = new Comprobante
                {
                    OrdenID = orden.OrdenID,
                    TipoDoc = tipoDoc,
                    Serie = serie,
                    Correlativo = correlativo,
                    FechaEmision = fechaEmision,
                    RucEmisor = company.Ruc,
                    RazonSocialEmisor = company.RazonSocial,
                    TipoDocCliente = client.TipoDoc,
                    NumDocCliente = client.NumDoc,
                    RznSocialCliente = client.RznSocial,
                    DireccionCliente = client.Address?.Direccion,
                    MontoOperacionesGravadas = totalGravadas,
                    MontoIgv = totalIgv,
                    MontoTotal = totalGeneral,
                    EstadoSunat = estadoSunat,
                    CodigoRespuesta = codigoRespuesta,
                    MensajeSunat = mensajeSunat,
                    HashCdr = hashCdr,
                    NombreArchivo = nombreArchivo
                };

                _db.Comprobantes.Add(comprobante);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Comprobante {Numero} registrado con estado {Estado}.", comprobante.NumeroCompleto, comprobante.EstadoSunat);
                return (true, comprobante, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al emitir comprobante para OrdenId: {OrdenId}", req.OrdenId);
                return (false, null, $"Error interno al emitir comprobante: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 2. EMITIR NOTA DE CRÉDITO (UBL 2.1 - TIPO 07)
        // ─────────────────────────────────────────────────────────────
        public async Task<(bool Ok, Comprobante? NotaCredito, string? Error)> EmitirNotaCreditoAsync(EmitirNotaCreditoRequest req)
        {
            try
            {
                var original = await _db.Comprobantes
                    .Include(c => c.Orden)
                        .ThenInclude(o => o!.Detalles!)
                            .ThenInclude(d => d.Servicio)
                    .Include(c => c.Orden!.Cliente)
                    .FirstOrDefaultAsync(c => c.ComprobanteID == req.ComprobanteId);

                if (original == null)
                    return (false, null, "Comprobante original a modificar no encontrado.");

                if (original.TipoDoc != "01" && original.TipoDoc != "03")
                    return (false, null, "Solo se pueden emitir notas de crédito para Facturas (01) o Boletas (03).");

                if (original.EstadoSunat == "Anulado con Nota de Crédito")
                    return (false, null, $"El comprobante {original.NumeroCompleto} ya ha sido anulado anteriormente.");

                // Determinar Serie para Nota de Crédito:
                // Factura (01) -> FC01
                // Boleta (03)  -> BC01
                var serie = !string.IsNullOrWhiteSpace(req.Serie)
                    ? req.Serie.Trim().ToUpper()
                    : (original.TipoDoc == "01"
                        ? (_config["FacturacionApi:SerieNotaCreditoFactura"] ?? "FC01")
                        : (_config["FacturacionApi:SerieNotaCreditoBoleta"] ?? "BC01"));

                var correlativo = await ObtenerSiguienteCorrelativoAsync("07", serie);
                var fechaEmision = DateTime.Now;

                var codMotivo = string.IsNullOrWhiteSpace(req.CodMotivo) ? "01" : req.CodMotivo.Trim();
                var desMotivo = !string.IsNullOrWhiteSpace(req.DesMotivo)
                    ? req.DesMotivo.Trim()
                    : (CatalogoSunatNotas.MotivosNotaCredito.TryGetValue(codMotivo, out var desCat) ? desCat : "Anulación de la operación");

                var company = ObtenerDatosEmpresa();
                var (details, totalGravadas, totalIgv, totalGeneral) = ConstruirDetallesUbl(original.Orden?.Detalles ?? Enumerable.Empty<DetalleOrden>());

                var numDocfectado = $"{original.Serie}-{original.Correlativo}";

                var noteRequest = new NoteRequest
                {
                    UblVersion = "2.1",
                    TipoDoc = "07",
                    Serie = serie,
                    Correlativo = correlativo.ToString(CultureInfo.InvariantCulture),
                    FechaEmision = fechaEmision.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                    TipDocAfectado = original.TipoDoc,
                    NumDocfectado = numDocfectado,
                    CodMotivo = codMotivo,
                    DesMotivo = desMotivo,
                    FormaPago = new FormaPagoDto { Moneda = "PEN", Tipo = "Contado" },
                    TipoMoneda = "PEN",
                    Company = company,
                    Client = new ClientDto
                    {
                        TipoDoc = original.TipoDocCliente,
                        NumDoc = original.NumDocCliente ?? "00000000",
                        RznSocial = original.RznSocialCliente ?? "CLIENTES VARIOS",
                        Address = new AddressDto { Direccion = original.DireccionCliente ?? "-" }
                    },
                    Details = details
                };

                var token = await ObtenerTokenAsync();
                bool exitoApi = false;
                string estadoSunat = "Pendiente";
                string? codigoRespuesta = "0000";
                string? mensajeSunat = "Nota de Crédito registrada localmente (Pendiente de sincronización SUNAT)";
                string? hashCdr = null;
                string? nombreArchivo = $"{company.Ruc}-07-{serie}-{correlativo}";

                if (!string.IsNullOrWhiteSpace(token))
                {
                    try
                    {
                        var res = await EnviarNotaApiAsync(noteRequest, token);
                        exitoApi = res.Exito;
                        estadoSunat = res.Estado;
                        codigoRespuesta = res.Codigo;
                        mensajeSunat = res.Mensaje;
                        hashCdr = res.Hash;
                        nombreArchivo = res.NombreArchivo;
                    }
                    catch (Exception exApi)
                    {
                        _logger.LogWarning(exApi, "Fallo al comunicar con la API para Nota de Crédito {Serie}-{Correlativo}. Se registra como Pendiente.", serie, correlativo);
                    }
                }
                else
                {
                    _logger.LogWarning("Sin conexión a la API externa. Registrando Nota de Crédito {Serie}-{Correlativo} en modo local.", serie, correlativo);
                }

                var notaCredito = new Comprobante
                {
                    OrdenID = original.OrdenID,
                    TipoDoc = "07",
                    Serie = serie,
                    Correlativo = correlativo,
                    FechaEmision = fechaEmision,
                    RucEmisor = company.Ruc,
                    RazonSocialEmisor = company.RazonSocial,
                    TipoDocCliente = original.TipoDocCliente,
                    NumDocCliente = original.NumDocCliente,
                    RznSocialCliente = original.RznSocialCliente,
                    DireccionCliente = original.DireccionCliente,
                    MontoOperacionesGravadas = totalGravadas,
                    MontoIgv = totalIgv,
                    MontoTotal = totalGeneral,
                    EstadoSunat = estadoSunat,
                    CodigoRespuesta = codigoRespuesta,
                    MensajeSunat = mensajeSunat,
                    HashCdr = hashCdr,
                    NombreArchivo = nombreArchivo,
                    TipDocAfectado = original.TipoDoc,
                    NumDocAfectado = original.NumeroCompleto,
                    CodMotivo = codMotivo,
                    DesMotivo = desMotivo,
                    ComprobanteReferenciaID = original.ComprobanteID
                };

                _db.Comprobantes.Add(notaCredito);

                // Si es anulación de la operación o devolución total, marcar comprobante original
                if (codMotivo == "01" || codMotivo == "02" || codMotivo == "06")
                {
                    original.EstadoSunat = "Anulado con Nota de Crédito";
                }

                await _db.SaveChangesAsync();

                _logger.LogInformation("Nota de Crédito {Numero} registrada con éxito para {Afectado}.", notaCredito.NumeroCompleto, original.NumeroCompleto);
                return (true, notaCredito, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al emitir Nota de Crédito para ComprobanteId: {ComprobanteId}", req.ComprobanteId);
                return (false, null, $"Error interno al emitir Nota de Crédito: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 3. DESCARGA DE PDF (INVOICES Y NOTES)
        // ─────────────────────────────────────────────────────────────
        public async Task<(byte[]? Archivo, string ContentType, string NombreArchivo, string? Error)> ObtenerPdfAsync(int comprobanteId)
        {
            var comprobante = await _db.Comprobantes
                .Include(c => c.Orden)
                    .ThenInclude(o => o!.Detalles!)
                        .ThenInclude(d => d.Servicio)
                .Include(c => c.Orden!.Cliente)
                .Include(c => c.ComprobanteReferencia)
                .FirstOrDefaultAsync(c => c.ComprobanteID == comprobanteId);

            if (comprobante == null)
                return (null, "", "", "Comprobante no encontrado.");

            var nombreArchivo = $"{comprobante.RucEmisor}-{comprobante.TipoDoc}-{comprobante.Serie}-{comprobante.Correlativo}.pdf";

            try
            {
                var token = await ObtenerTokenAsync();
                var baseUrl = (_config["FacturacionApi:BaseUrl"] ?? "http://greenter.test/api").TrimEnd('/');
                var client = _httpClientFactory.CreateClient();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpContent content;
                string endpointUrl;

                if (comprobante.TipoDoc == "07" || comprobante.TipoDoc == "08")
                {
                    var noteReq = ReconstruirNoteRequest(comprobante);
                    var jsonBody = JsonSerializer.Serialize(noteReq);
                    content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    endpointUrl = $"{baseUrl}/notes/pdf";
                }
                else
                {
                    var invoiceReq = ReconstruirInvoiceRequest(comprobante);
                    var jsonBody = JsonSerializer.Serialize(invoiceReq);
                    content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    endpointUrl = $"{baseUrl}/invoices/pdf";
                }

                var response = await client.PostAsync(endpointUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";
                    return (bytes, contentType, nombreArchivo, null);
                }

                var errText = await response.Content.ReadAsStringAsync();
                return (null, "", nombreArchivo, $"API respondió {(int)response.StatusCode}: {errText}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener PDF de comprobante {Id}", comprobanteId);
                return (null, "", nombreArchivo, ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 4. DESCARGA DE XML (INVOICES Y NOTES)
        // ─────────────────────────────────────────────────────────────
        public async Task<(string? Xml, string NombreArchivo, string? Error)> ObtenerXmlAsync(int comprobanteId)
        {
            var comprobante = await _db.Comprobantes
                .Include(c => c.Orden)
                    .ThenInclude(o => o!.Detalles!)
                        .ThenInclude(d => d.Servicio)
                .Include(c => c.Orden!.Cliente)
                .Include(c => c.ComprobanteReferencia)
                .FirstOrDefaultAsync(c => c.ComprobanteID == comprobanteId);

            if (comprobante == null)
                return (null, "", "Comprobante no encontrado.");

            var nombreArchivo = $"{comprobante.RucEmisor}-{comprobante.TipoDoc}-{comprobante.Serie}-{comprobante.Correlativo}.xml";

            if (!string.IsNullOrWhiteSpace(comprobante.XmlFirmado))
                return (comprobante.XmlFirmado, nombreArchivo, null);

            try
            {
                var token = await ObtenerTokenAsync();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    var baseUrl = (_config["FacturacionApi:BaseUrl"] ?? "http://greenter.test/api").TrimEnd('/');
                    var client = _httpClientFactory.CreateClient();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    HttpContent content;
                    string endpointUrl;

                    if (comprobante.TipoDoc == "07" || comprobante.TipoDoc == "08")
                    {
                        var noteReq = ReconstruirNoteRequest(comprobante);
                        var jsonBody = JsonSerializer.Serialize(noteReq);
                        content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                        endpointUrl = $"{baseUrl}/notes/xml";
                    }
                    else
                    {
                        var invoiceReq = ReconstruirInvoiceRequest(comprobante);
                        var jsonBody = JsonSerializer.Serialize(invoiceReq);
                        content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                        endpointUrl = $"{baseUrl}/invoices/xml";
                    }

                    var response = await client.PostAsync(endpointUrl, content);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseText = await response.Content.ReadAsStringAsync();
                        string? xml = null;
                        try
                        {
                            using var doc = JsonDocument.Parse(responseText);
                            if (doc.RootElement.TryGetProperty("xml", out var xmlProp))
                            {
                                xml = xmlProp.GetString();
                            }
                        }
                        catch
                        {
                            xml = responseText;
                        }

                        if (!string.IsNullOrWhiteSpace(xml))
                        {
                            comprobante.XmlFirmado = xml;
                            await _db.SaveChangesAsync();
                            return (xml, nombreArchivo, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "API remota no disponible para XML de comprobante {Id}. Se generará UBL 2.1 local.", comprobanteId);
            }

            // Fallback: Generar XML estándar UBL 2.1 localmente
            var localXml = GenerarUblXmlLocal(comprobante);
            comprobante.XmlFirmado = localXml;
            await _db.SaveChangesAsync();
            return (localXml, nombreArchivo, null);
        }

        public async Task<int> ObtenerSiguienteCorrelativoAsync(string tipoDoc, string serie)
        {
            var maxCorrelativo = await _db.Comprobantes
                .Where(c => c.TipoDoc == tipoDoc && c.Serie == serie)
                .MaxAsync(c => (int?)c.Correlativo) ?? 0;

            return maxCorrelativo + 1;
        }

        public async Task<Comprobante?> ObtenerPorOrdenIdAsync(int ordenId)
        {
            return await _db.Comprobantes
                .Where(c => c.OrdenID == ordenId)
                .OrderByDescending(c => c.ComprobanteID)
                .FirstOrDefaultAsync();
        }

        public async Task<Comprobante?> ObtenerPorIdAsync(int comprobanteId)
        {
            return await _db.Comprobantes
                .Include(c => c.Orden)
                    .ThenInclude(o => o!.Detalles!)
                        .ThenInclude(d => d.Servicio)
                .Include(c => c.Orden!.Cliente)
                .Include(c => c.ComprobanteReferencia)
                .FirstOrDefaultAsync(c => c.ComprobanteID == comprobanteId);
        }

        public async Task<List<Comprobante>> ListarComprobantesAsync(
            string? tipoDoc = null,
            string? estado = null,
            string? buscar = null,
            DateTime? fechaInicio = null,
            DateTime? fechaFin = null)
        {
            var query = _db.Comprobantes
                .Include(c => c.Orden)
                    .ThenInclude(o => o!.Cliente)
                .Include(c => c.ComprobanteReferencia)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(tipoDoc) && tipoDoc != "TODOS")
            {
                query = query.Where(c => c.TipoDoc == tipoDoc);
            }

            if (!string.IsNullOrWhiteSpace(estado) && estado != "TODOS")
            {
                query = query.Where(c => c.EstadoSunat == estado);
            }

            if (fechaInicio.HasValue)
            {
                var inicio = fechaInicio.Value.Date;
                query = query.Where(c => c.FechaEmision >= inicio);
            }

            if (fechaFin.HasValue)
            {
                var fin = fechaFin.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(c => c.FechaEmision <= fin);
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                var b = buscar.Trim().ToLower();
                query = query.Where(c =>
                    c.Serie.ToLower().Contains(b) ||
                    c.Correlativo.ToString().Contains(b) ||
                    (c.NumDocCliente != null && c.NumDocCliente.Contains(b)) ||
                    (c.RznSocialCliente != null && c.RznSocialCliente.ToLower().Contains(b)) ||
                    (c.NumDocAfectado != null && c.NumDocAfectado.ToLower().Contains(b)));
            }

            return await query
                .OrderByDescending(c => c.FechaEmision)
                .ThenByDescending(c => c.ComprobanteID)
                .ToListAsync();
        }

        // ─────────────────────────────────────────────────────────────
        // MÉTODOS PRIVADOS DE APOYO
        // ─────────────────────────────────────────────────────────────

        private async Task<string?> ObtenerTokenAsync()
        {
            lock (_tokenLock)
            {
                if (!string.IsNullOrWhiteSpace(_cachedToken) && DateTime.UtcNow < _tokenExpiresAt)
                    return _cachedToken;
            }

            var baseUrl = (_config["FacturacionApi:BaseUrl"] ?? "http://greenter.test/api").TrimEnd('/');
            var email = _config["FacturacionApi:AuthEmail"] ?? "mondragonmaycol541@gmail.com";
            var password = _config["FacturacionApi:AuthPassword"] ?? "12345678";

            try
            {
                var client = _httpClientFactory.CreateClient();
                var loginData = new Dictionary<string, string>
                {
                    { "email", email },
                    { "password", password }
                };

                using var formContent = new FormUrlEncodedContent(loginData);
                var response = await client.PostAsync($"{baseUrl}/login", formContent);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Fallo en login de Facturación API. Status: {Status}", response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? token = null;
                if (root.TryGetProperty("token", out var tokenProp))
                    token = tokenProp.GetString();
                else if (root.TryGetProperty("access_token", out var accessProp))
                    token = accessProp.GetString();

                if (!string.IsNullOrWhiteSpace(token))
                {
                    lock (_tokenLock)
                    {
                        _cachedToken = token;
                        _tokenExpiresAt = DateTime.UtcNow.AddHours(6);
                    }
                }

                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al conectar con el endpoint de login de la API de facturación.");
                return null;
            }
        }

        private async Task<(bool Exito, string Estado, string? Codigo, string? Mensaje, string? Hash, string? NombreArchivo)>
            EnviarFacturaApiAsync(InvoiceRequest invoice, string token)
        {
            var baseUrl = (_config["FacturacionApi:BaseUrl"] ?? "http://greenter.test/api").TrimEnd('/');
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var jsonBody = JsonSerializer.Serialize(invoice);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{baseUrl}/invoices/send", content);
            var responseText = await response.Content.ReadAsStringAsync();

            var nombreArchivo = $"{invoice.Company.Ruc}-{invoice.TipoDoc}-{invoice.Serie}-{invoice.Correlativo}";

            if (!response.IsSuccessStatusCode)
            {
                return (false, "Error", ((int)response.StatusCode).ToString(), responseText, null, nombreArchivo);
            }

            return InterpretarRespuestaSunat(responseText, nombreArchivo);
        }

        private async Task<(bool Exito, string Estado, string? Codigo, string? Mensaje, string? Hash, string? NombreArchivo)>
            EnviarNotaApiAsync(NoteRequest note, string token)
        {
            var baseUrl = (_config["FacturacionApi:BaseUrl"] ?? "http://greenter.test/api").TrimEnd('/');
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var jsonBody = JsonSerializer.Serialize(note);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{baseUrl}/notes/send", content);
            var responseText = await response.Content.ReadAsStringAsync();

            var nombreArchivo = $"{note.Company.Ruc}-{note.TipoDoc}-{note.Serie}-{note.Correlativo}";

            if (!response.IsSuccessStatusCode)
            {
                return (false, "Error", ((int)response.StatusCode).ToString(), responseText, null, nombreArchivo);
            }

            return InterpretarRespuestaSunat(responseText, nombreArchivo);
        }

        private static (bool Exito, string Estado, string? Codigo, string? Mensaje, string? Hash, string? NombreArchivo)
            InterpretarRespuestaSunat(string responseText, string nombreArchivo)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                var root = doc.RootElement;

                bool success = true;
                string estado = "Aceptado";
                string? codigo = "0";
                string? mensaje = "Comprobante aceptado por SUNAT";
                string? hash = null;

                if (root.TryGetProperty("hash", out var hashProp))
                    hash = hashProp.GetString();

                if (root.TryGetProperty("sunatResponse", out var sunatProp))
                {
                    if (sunatProp.TryGetProperty("success", out var sucProp))
                        success = sucProp.GetBoolean();

                    if (sunatProp.TryGetProperty("cdrResponse", out var cdrProp))
                    {
                        if (cdrProp.TryGetProperty("code", out var codeProp))
                            codigo = codeProp.GetString();
                        if (cdrProp.TryGetProperty("description", out var descProp))
                            mensaje = descProp.GetString();
                    }
                }
                else if (root.TryGetProperty("message", out var msgProp))
                {
                    mensaje = msgProp.GetString();
                }

                estado = success && (codigo == "0" || string.IsNullOrWhiteSpace(codigo)) ? "Aceptado" : "Rechazado";
                return (success, estado, codigo, mensaje, hash, nombreArchivo);
            }
            catch
            {
                return (true, "Aceptado", "0", "Aceptado por SUNAT", null, nombreArchivo);
            }
        }

        private CompanyDto ObtenerDatosEmpresa()
        {
            var sec = _config.GetSection("FacturacionApi:Empresa");
            return new CompanyDto
            {
                Ruc = sec["Ruc"] ?? "10708464100",
                RazonSocial = sec["RazonSocial"] ?? "MONDRAGON DELGADO MAYCOL",
                NombreComercial = sec["NombreComercial"] ?? "APP LAVALUC",
                Address = new AddressDto
                {
                    Ubigueo = sec["Ubigeo"] ?? "150101",
                    Departamento = sec["Departamento"] ?? "LIMA",
                    Provincia = sec["Provincia"] ?? "LIMA",
                    Distrito = sec["Distrito"] ?? "LIMA",
                    Urbanizacion = sec["Urbanizacion"] ?? "-",
                    Direccion = sec["Direccion"] ?? "Av. Villa Nueva 221",
                    CodLocal = sec["CodLocal"] ?? "0000"
                }
            };
        }

        private static ClientDto ConstruirClienteReceptor(EmitirComprobanteRequest req, Cliente? cliente, string tipoDoc)
        {
            if (tipoDoc == "01") // FACTURA -> REQUIERE RUC
            {
                var ruc = !string.IsNullOrWhiteSpace(req.NumDocCliente)
                    ? req.NumDocCliente.Trim()
                    : (cliente?.Dni != null && cliente.Dni.Length == 11 ? cliente.Dni : "00000000000");

                var razonSocial = !string.IsNullOrWhiteSpace(req.RznSocialCliente)
                    ? req.RznSocialCliente.Trim()
                    : (cliente?.NombreCompleto ?? "CLIENTE FACTURA");

                return new ClientDto
                {
                    TipoDoc = "6", // 6 = RUC
                    NumDoc = ruc,
                    RznSocial = razonSocial,
                    Address = new AddressDto
                    {
                        Direccion = !string.IsNullOrWhiteSpace(req.DireccionCliente) ? req.DireccionCliente : "LIMA"
                    }
                };
            }
            else // BOLETA (03)
            {
                var dni = !string.IsNullOrWhiteSpace(req.NumDocCliente)
                    ? req.NumDocCliente.Trim()
                    : cliente?.Dni;

                if (!string.IsNullOrWhiteSpace(dni) && dni.Length == 8)
                {
                    return new ClientDto
                    {
                        TipoDoc = "1", // 1 = DNI
                        NumDoc = dni,
                        RznSocial = !string.IsNullOrWhiteSpace(req.RznSocialCliente)
                            ? req.RznSocialCliente.Trim()
                            : (cliente?.NombreCompleto ?? "CLIENTE"),
                        Address = new AddressDto { Direccion = req.DireccionCliente ?? "-" }
                    };
                }

                // Sin Documento (Clientes varios)
                return new ClientDto
                {
                    TipoDoc = "0",
                    NumDoc = "00000000",
                    RznSocial = !string.IsNullOrWhiteSpace(req.RznSocialCliente)
                        ? req.RznSocialCliente.Trim()
                        : (cliente?.NombreCompleto ?? "CLIENTES VARIOS"),
                    Address = new AddressDto { Direccion = "-" }
                };
            }
        }

        private static (List<InvoiceDetailDto> Details, decimal Gravadas, decimal Igv, decimal Total)
            ConstruirDetallesUbl(IEnumerable<DetalleOrden> detalles)
        {
            var list = new List<InvoiceDetailDto>();
            decimal sumGravadas = 0;
            decimal sumIgv = 0;
            decimal sumTotal = 0;

            foreach (var d in detalles)
            {
                if (d.Cantidad <= 0) continue;

                var totalLinea = d.Total;
                var cant = (decimal)d.Cantidad;

                var precioUnitario = Math.Round(totalLinea / cant, 2);
                var valorUnitario = Math.Round(precioUnitario / 1.18m, 6);
                var valorVenta = Math.Round(valorUnitario * cant, 2);
                var baseIgv = valorVenta;
                var igv = Math.Round(totalLinea - valorVenta, 2);

                sumGravadas += valorVenta;
                sumIgv += igv;
                sumTotal += totalLinea;

                var desc = d.Servicio?.NombreServicio ?? "Servicio de Lavandería";
                var unidad = (d.Servicio?.UnidadMedida ?? "NIU").Trim().ToUpper();
                if (unidad.Contains("KILO") || unidad.Contains("KG"))
                    unidad = "KGM";
                else
                    unidad = "NIU";

                list.Add(new InvoiceDetailDto
                {
                    TipAfeIgv = 10, // Gravado - Operación Onerosa
                    CodProducto = $"SERV-{d.ServicioID}",
                    Unidad = unidad,
                    Descripcion = desc,
                    Cantidad = cant,
                    MtoValorUnitario = valorUnitario,
                    MtoValorVenta = valorVenta,
                    MtoBaseIgv = baseIgv,
                    PorcentajeIgv = 18,
                    Igv = igv,
                    TotalImpuestos = igv,
                    MtoPrecioUnitario = precioUnitario
                });
            }

            return (list, sumGravadas, sumIgv, sumTotal);
        }

        private InvoiceRequest ReconstruirInvoiceRequest(Comprobante comprobante)
        {
            var company = ObtenerDatosEmpresa();
            var (details, _, _, _) = ConstruirDetallesUbl(comprobante.Orden?.Detalles ?? Enumerable.Empty<DetalleOrden>());

            if (!details.Any())
            {
                var total = comprobante.MontoTotal > 0 ? comprobante.MontoTotal : 10m;
                var valorVenta = Math.Round(total / 1.18m, 2);
                var igv = total - valorVenta;
                details.Add(new InvoiceDetailDto
                {
                    CodProducto = "P001",
                    Unidad = "NIU",
                    Cantidad = 1,
                    Descripcion = "Servicio de Lavandería",
                    MtoBaseIgv = valorVenta,
                    PorcentajeIgv = 18,
                    Igv = igv,
                    TotalImpuestos = igv,
                    TipAfeIgv = 10,
                    MtoValorVenta = valorVenta,
                    MtoValorUnitario = valorVenta,
                    MtoPrecioUnitario = total
                });
            }

            var numDocCliente = !string.IsNullOrWhiteSpace(comprobante.NumDocCliente)
                ? comprobante.NumDocCliente
                : (comprobante.TipoDoc == "01" ? "10708464100" : "00000000");

            var tipoDocCliente = !string.IsNullOrWhiteSpace(comprobante.TipoDocCliente)
                ? comprobante.TipoDocCliente
                : (comprobante.TipoDoc == "01" ? "6" : "1");

            return new InvoiceRequest
            {
                UblVersion = "2.1",
                TipoDoc = comprobante.TipoDoc,
                TipoOperacion = "0101",
                Serie = comprobante.Serie,
                Correlativo = comprobante.Correlativo.ToString(CultureInfo.InvariantCulture),
                FechaEmision = comprobante.FechaEmision.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                FormaPago = new FormaPagoDto { Moneda = "PEN", Tipo = "Contado" },
                TipoMoneda = "PEN",
                Company = company,
                Client = new ClientDto
                {
                    TipoDoc = tipoDocCliente,
                    NumDoc = numDocCliente,
                    RznSocial = comprobante.RznSocialCliente ?? "CLIENTES VARIOS",
                    Address = new AddressDto { Direccion = comprobante.DireccionCliente ?? "-" }
                },
                Details = details
            };
        }

        private NoteRequest ReconstruirNoteRequest(Comprobante comprobante)
        {
            var company = ObtenerDatosEmpresa();
            var (details, _, _, _) = ConstruirDetallesUbl(comprobante.Orden?.Detalles ?? Enumerable.Empty<DetalleOrden>());

            if (!details.Any())
            {
                var total = comprobante.MontoTotal > 0 ? comprobante.MontoTotal : 10m;
                var valorVenta = Math.Round(total / 1.18m, 2);
                var igv = total - valorVenta;
                details.Add(new InvoiceDetailDto
                {
                    CodProducto = "P001",
                    Unidad = "NIU",
                    Cantidad = 1,
                    Descripcion = "Servicio de Lavandería",
                    MtoBaseIgv = valorVenta,
                    PorcentajeIgv = 18,
                    Igv = igv,
                    TotalImpuestos = igv,
                    TipAfeIgv = 10,
                    MtoValorVenta = valorVenta,
                    MtoValorUnitario = valorVenta,
                    MtoPrecioUnitario = total
                });
            }

            var tipDocAfectado = comprobante.TipDocAfectado ?? (comprobante.Serie.StartsWith("F", StringComparison.OrdinalIgnoreCase) ? "01" : "03");
            var numDocAfectado = comprobante.NumDocAfectado ?? comprobante.ComprobanteReferencia?.NumeroCompleto ?? "F001-1";

            var numDocCliente = !string.IsNullOrWhiteSpace(comprobante.NumDocCliente)
                ? comprobante.NumDocCliente
                : (tipDocAfectado == "01" ? "10708464100" : "00000000");

            var tipoDocCliente = !string.IsNullOrWhiteSpace(comprobante.TipoDocCliente)
                ? comprobante.TipoDocCliente
                : (tipDocAfectado == "01" ? "6" : "1");

            return new NoteRequest
            {
                UblVersion = "2.1",
                TipoDoc = comprobante.TipoDoc,
                Serie = comprobante.Serie,
                Correlativo = comprobante.Correlativo.ToString(CultureInfo.InvariantCulture),
                FechaEmision = comprobante.FechaEmision.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                TipDocAfectado = tipDocAfectado,
                NumDocfectado = numDocAfectado,
                CodMotivo = comprobante.CodMotivo ?? "01",
                DesMotivo = comprobante.DesMotivo ?? "Anulación de la operación",
                FormaPago = new FormaPagoDto { Moneda = "PEN", Tipo = "Contado" },
                TipoMoneda = "PEN",
                Company = company,
                Client = new ClientDto
                {
                    TipoDoc = tipoDocCliente,
                    NumDoc = numDocCliente,
                    RznSocial = comprobante.RznSocialCliente ?? "CLIENTES VARIOS",
                    Address = new AddressDto { Direccion = comprobante.DireccionCliente ?? "-" }
                },
                Details = details
            };
        }

        private string GenerarUblXmlLocal(Comprobante c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");

            if (c.TipoDoc == "07") // Nota de Crédito
            {
                sb.AppendLine("<CreditNote xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2\"");
                sb.AppendLine("            xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\"");
                sb.AppendLine("            xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\"");
                sb.AppendLine("            xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\"");
                sb.AppendLine("            xmlns:ext=\"urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2\">");
                sb.AppendLine("  <ext:UBLExtensions><ext:UBLExtension><ext:ExtensionContent/></ext:UBLExtension></ext:UBLExtensions>");
                sb.AppendLine("  <cbc:UBLVersionID>2.1</cbc:UBLVersionID>");
                sb.AppendLine("  <cbc:CustomizationID>2.0</cbc:CustomizationID>");
                sb.AppendLine($"  <cbc:ID>{c.NumeroCompleto}</cbc:ID>");
                sb.AppendLine($"  <cbc:IssueDate>{c.FechaEmision:yyyy-MM-dd}</cbc:IssueDate>");
                sb.AppendLine($"  <cbc:IssueTime>{c.FechaEmision:HH:mm:ss}</cbc:IssueTime>");
                sb.AppendLine("  <cbc:DocumentCurrencyCode>PEN</cbc:DocumentCurrencyCode>");
                sb.AppendLine("  <cac:DiscrepancyResponse>");
                sb.AppendLine($"    <cbc:ReferenceID>{c.NumDocAfectado}</cbc:ReferenceID>");
                sb.AppendLine($"    <cbc:ResponseCode>{c.CodMotivo ?? "01"}</cbc:ResponseCode>");
                sb.AppendLine($"    <cbc:Description><![CDATA[{c.DesMotivo ?? "Anulación de la operación"}]]></cbc:Description>");
                sb.AppendLine("  </cac:DiscrepancyResponse>");
                sb.AppendLine("  <cac:BillingReference>");
                sb.AppendLine("    <cac:InvoiceDocumentReference>");
                sb.AppendLine($"      <cbc:ID>{c.NumDocAfectado}</cbc:ID>");
                sb.AppendLine($"      <cbc:DocumentTypeCode>{c.TipDocAfectado ?? "03"}</cbc:DocumentTypeCode>");
                sb.AppendLine("    </cac:InvoiceDocumentReference>");
                sb.AppendLine("  </cac:BillingReference>");
                sb.AppendLine("  <cac:AccountingSupplierParty><cac:Party>");
                sb.AppendLine($"    <cac:PartyIdentification><cbc:ID schemeID=\"6\">{c.RucEmisor}</cbc:ID></cac:PartyIdentification>");
                sb.AppendLine($"    <cac:PartyLegalEntity><cbc:RegistrationName><![CDATA[{c.RazonSocialEmisor}]]></cbc:RegistrationName></cac:PartyLegalEntity>");
                sb.AppendLine("  </cac:Party></cac:AccountingSupplierParty>");
                sb.AppendLine("  <cac:AccountingCustomerParty><cac:Party>");
                sb.AppendLine($"    <cac:PartyIdentification><cbc:ID schemeID=\"{c.TipoDocCliente ?? "1"}\">{c.NumDocCliente ?? "00000000"}</cbc:ID></cac:PartyIdentification>");
                sb.AppendLine($"    <cac:PartyLegalEntity><cbc:RegistrationName><![CDATA[{c.RznSocialCliente ?? "CLIENTES VARIOS"}]]></cbc:RegistrationName></cac:PartyLegalEntity>");
                sb.AppendLine("  </cac:Party></cac:AccountingCustomerParty>");
                sb.AppendLine("  <cac:TaxTotal>");
                sb.AppendLine($"    <cbc:TaxAmount currencyID=\"PEN\">{c.MontoIgv:F2}</cbc:TaxAmount>");
                sb.AppendLine("    <cac:TaxSubtotal>");
                sb.AppendLine($"      <cbc:TaxableAmount currencyID=\"PEN\">{c.MontoOperacionesGravadas:F2}</cbc:TaxableAmount>");
                sb.AppendLine($"      <cbc:TaxAmount currencyID=\"PEN\">{c.MontoIgv:F2}</cbc:TaxAmount>");
                sb.AppendLine("      <cac:TaxCategory><cac:TaxScheme><cbc:ID>1000</cbc:ID><cbc:Name>IGV</cbc:Name><cbc:TaxTypeCode>VAT</cbc:TaxTypeCode></cac:TaxScheme></cac:TaxCategory>");
                sb.AppendLine("    </cac:TaxSubtotal>");
                sb.AppendLine("  </cac:TaxTotal>");
                sb.AppendLine("  <cac:LegalMonetaryTotal>");
                sb.AppendLine($"    <cbc:PayableAmount currencyID=\"PEN\">{c.MontoTotal:F2}</cbc:PayableAmount>");
                sb.AppendLine("  </cac:LegalMonetaryTotal>");
                sb.AppendLine("</CreditNote>");
            }
            else // Factura o Boleta
            {
                sb.AppendLine("<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"");
                sb.AppendLine("         xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\"");
                sb.AppendLine("         xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\"");
                sb.AppendLine("         xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\"");
                sb.AppendLine("         xmlns:ext=\"urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2\">");
                sb.AppendLine("  <ext:UBLExtensions><ext:UBLExtension><ext:ExtensionContent/></ext:UBLExtension></ext:UBLExtensions>");
                sb.AppendLine("  <cbc:UBLVersionID>2.1</cbc:UBLVersionID>");
                sb.AppendLine("  <cbc:CustomizationID>2.0</cbc:CustomizationID>");
                sb.AppendLine($"  <cbc:ID>{c.NumeroCompleto}</cbc:ID>");
                sb.AppendLine($"  <cbc:IssueDate>{c.FechaEmision:yyyy-MM-dd}</cbc:IssueDate>");
                sb.AppendLine($"  <cbc:IssueTime>{c.FechaEmision:HH:mm:ss}</cbc:IssueTime>");
                sb.AppendLine($"  <cbc:InvoiceTypeCode listID=\"0101\">{c.TipoDoc}</cbc:InvoiceTypeCode>");
                sb.AppendLine("  <cbc:DocumentCurrencyCode>PEN</cbc:DocumentCurrencyCode>");
                sb.AppendLine("  <cac:AccountingSupplierParty><cac:Party>");
                sb.AppendLine($"    <cac:PartyIdentification><cbc:ID schemeID=\"6\">{c.RucEmisor}</cbc:ID></cac:PartyIdentification>");
                sb.AppendLine($"    <cac:PartyLegalEntity><cbc:RegistrationName><![CDATA[{c.RazonSocialEmisor}]]></cbc:RegistrationName></cac:PartyLegalEntity>");
                sb.AppendLine("  </cac:Party></cac:AccountingSupplierParty>");
                sb.AppendLine("  <cac:AccountingCustomerParty><cac:Party>");
                sb.AppendLine($"    <cac:PartyIdentification><cbc:ID schemeID=\"{c.TipoDocCliente ?? (c.TipoDoc == "01" ? "6" : "1")}\">{c.NumDocCliente ?? "00000000"}</cbc:ID></cac:PartyIdentification>");
                sb.AppendLine($"    <cac:PartyLegalEntity><cbc:RegistrationName><![CDATA[{c.RznSocialCliente ?? "CLIENTES VARIOS"}]]></cbc:RegistrationName></cac:PartyLegalEntity>");
                sb.AppendLine("  </cac:Party></cac:AccountingCustomerParty>");
                sb.AppendLine("  <cac:TaxTotal>");
                sb.AppendLine($"    <cbc:TaxAmount currencyID=\"PEN\">{c.MontoIgv:F2}</cbc:TaxAmount>");
                sb.AppendLine("    <cac:TaxSubtotal>");
                sb.AppendLine($"      <cbc:TaxableAmount currencyID=\"PEN\">{c.MontoOperacionesGravadas:F2}</cbc:TaxableAmount>");
                sb.AppendLine($"      <cbc:TaxAmount currencyID=\"PEN\">{c.MontoIgv:F2}</cbc:TaxAmount>");
                sb.AppendLine("      <cac:TaxCategory><cac:TaxScheme><cbc:ID>1000</cbc:ID><cbc:Name>IGV</cbc:Name><cbc:TaxTypeCode>VAT</cbc:TaxTypeCode></cac:TaxScheme></cac:TaxCategory>");
                sb.AppendLine("    </cac:TaxSubtotal>");
                sb.AppendLine("  </cac:TaxTotal>");
                sb.AppendLine("  <cac:LegalMonetaryTotal>");
                sb.AppendLine($"    <cbc:LineExtensionAmount currencyID=\"PEN\">{c.MontoOperacionesGravadas:F2}</cbc:LineExtensionAmount>");
                sb.AppendLine($"    <cbc:TaxInclusiveAmount currencyID=\"PEN\">{c.MontoTotal:F2}</cbc:TaxInclusiveAmount>");
                sb.AppendLine($"    <cbc:PayableAmount currencyID=\"PEN\">{c.MontoTotal:F2}</cbc:PayableAmount>");
                sb.AppendLine("  </cac:LegalMonetaryTotal>");

                if (c.Orden?.Detalles != null && c.Orden.Detalles.Any())
                {
                    int itemIdx = 1;
                    foreach (var det in c.Orden.Detalles)
                    {
                        var cant = det.Cantidad > 0 ? (decimal)det.Cantidad : 1m;
                        var valVenta = Math.Round(det.Total / 1.18m, 2);
                        var valUnit = Math.Round(valVenta / cant, 4);
                        var precioUnit = Math.Round(det.Total / cant, 2);
                        var igvLine = Math.Round(det.Total - valVenta, 2);

                        sb.AppendLine("  <cac:InvoiceLine>");
                        sb.AppendLine($"    <cbc:ID>{itemIdx++}</cbc:ID>");
                        sb.AppendLine($"    <cbc:InvoicedQuantity unitCode=\"NIU\">{cant:F0}</cbc:InvoicedQuantity>");
                        sb.AppendLine($"    <cbc:LineExtensionAmount currencyID=\"PEN\">{valVenta:F2}</cbc:LineExtensionAmount>");
                        sb.AppendLine("    <cac:PricingReference><cac:AlternativeConditionPrice>");
                        sb.AppendLine($"      <cbc:PriceAmount currencyID=\"PEN\">{precioUnit:F2}</cbc:PriceAmount>");
                        sb.AppendLine("      <cbc:PriceTypeCode>01</cbc:PriceTypeCode>");
                        sb.AppendLine("    </cac:AlternativeConditionPrice></cac:PricingReference>");
                        sb.AppendLine("    <cac:TaxTotal>");
                        sb.AppendLine($"      <cbc:TaxAmount currencyID=\"PEN\">{igvLine:F2}</cbc:TaxAmount>");
                        sb.AppendLine("      <cac:TaxSubtotal>");
                        sb.AppendLine($"        <cbc:TaxableAmount currencyID=\"PEN\">{valVenta:F2}</cbc:TaxableAmount>");
                        sb.AppendLine($"        <cbc:TaxAmount currencyID=\"PEN\">{igvLine:F2}</cbc:TaxAmount>");
                        sb.AppendLine("        <cac:TaxCategory><cbc:Percent>18</cbc:Percent><cac:TaxScheme><cbc:ID>1000</cbc:ID><cbc:Name>IGV</cbc:Name><cbc:TaxTypeCode>VAT</cbc:TaxTypeCode></cac:TaxScheme></cac:TaxCategory>");
                        sb.AppendLine("      </cac:TaxSubtotal>");
                        sb.AppendLine("    </cac:TaxTotal>");
                        sb.AppendLine("    <cac:Item>");
                        sb.AppendLine($"      <cbc:Description><![CDATA[{det.Servicio?.NombreServicio ?? "Servicio de Lavandería"}]]></cbc:Description>");
                        sb.AppendLine("    </cac:Item>");
                        sb.AppendLine("    <cac:Price>");
                        sb.AppendLine($"      <cbc:PriceAmount currencyID=\"PEN\">{valUnit:F4}</cbc:PriceAmount>");
                        sb.AppendLine("    </cac:Price>");
                        sb.AppendLine("  </cac:InvoiceLine>");
                    }
                }

                sb.AppendLine("</Invoice>");
            }

            return sb.ToString();
        }
    }
}
