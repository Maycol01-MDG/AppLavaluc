using AppLavaluc.Data;
using AppLavaluc.Models;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Services
{
    /// <summary>
    /// Servicio de órdenes: concentra toda la lógica de negocio.
    /// El controlador solo orquesta, este servicio hace el trabajo.
    /// </summary>
    public class OrdenService : IOrdenService
    {
        private readonly LavanderiaContext _db;
        private readonly ILogger<OrdenService> _logger;
        private readonly IHostEnvironment _env;

        public OrdenService(LavanderiaContext db, ILogger<OrdenService> logger, IHostEnvironment env)
        {
            _db = db;
            _logger = logger;
            _env = env;
        }

        // ─────────────────────────────────────────────────────────────
        // CREAR ORDEN
        // ─────────────────────────────────────────────────────────────
        public async Task<(bool Ok, int OrdenId, string? Error)> CrearOrdenAsync(CrearOrdenRequest req)
        {
            if (req.ServicioIds.Count == 0)
                return (false, 0, "Debe agregar al menos un servicio a la orden.");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                try
                {
                    var nombre = (req.NombreCliente ?? "").Trim();
                    if (nombre.Length > 100) nombre = nombre[..100];

                    var apellidos = (req.ApellidosCliente ?? "").Trim();
                    if (apellidos.Length > 100) apellidos = apellidos[..100];

                    var telefono = req.TelefonoCliente?.Trim();
                    if (!string.IsNullOrWhiteSpace(telefono) && telefono.Length > 20) telefono = telefono[..20];

                    var cliente = await ObtenerOCrearClienteAsync(req.DniCliente, nombre, apellidos, telefono);

                    var observaciones = req.Observaciones?.Trim();
                    if (!string.IsNullOrEmpty(observaciones) && observaciones.Length > 500)
                    {
                        observaciones = observaciones[..500];
                    }

                    var tipoEntrega = (req.TipoEntrega ?? "").Trim();
                    if (tipoEntrega.Length > 30)
                    {
                        tipoEntrega = tipoEntrega[..30];
                    }

                    var orden = new Orden
                    {
                        ClienteID = cliente.ClienteID,
                        FechaRecepcion = DateTime.Now,
                        FechaEntregaEstimada = req.FechaEntregaEstimada,
                        Estado = "Recibido",
                        TipoEntrega = tipoEntrega,
                        Telefono = telefono,
                        Observaciones = observaciones,
                        EstadoRecojo = "Pendiente",
                        MontoTotal = 0,
                        MontoPagado = 0,
                        SaldoPendiente = 0,
                        EstadoPago = "Pendiente"
                    };

                    _db.Ordenes.Add(orden);
                    await _db.SaveChangesAsync();

                    decimal totalCalculado = await AgregarDetallesAsync(orden, req);

                    decimal montoPagado = Math.Max(0, Math.Min(req.MontoPagado, totalCalculado));
                    decimal saldoPendiente = totalCalculado - montoPagado;
                    string estadoPago = DeterminarEstadoPago(montoPagado, totalCalculado);

                    orden.MontoTotal = totalCalculado;
                    orden.MontoPagado = montoPagado;
                    orden.SaldoPendiente = saldoPendiente;
                    orden.EstadoPago = estadoPago;

                    if (montoPagado > 0)
                    {
                        _db.Pagos.Add(new Pago
                        {
                            OrdenID = orden.OrdenID,
                            Monto = montoPagado,
                            FechaPago = DateTime.Now,
                            MetodoPago = "Efectivo",
                            Notas = "Pago inicial al crear la orden"
                        });
                    }

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                    return (true, orden.OrdenID, (string?)null);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "Error al crear orden para cliente {Nombre}", req.NombreCliente);
                    var baseMsg = ex.GetBaseException().Message;
                    return (false, 0, _env.IsDevelopment() ? baseMsg : "Ocurrió un error interno al crear la orden.");
                }
            });
        }

        // ─────────────────────────────────────────────────────────────
        // ENTREGAR ORDEN
        // ─────────────────────────────────────────────────────────────
        public async Task<(bool Ok, string? Error)> EntregarOrdenAsync(int ordenId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                try
                {
                    var orden = await _db.Ordenes.FindAsync(ordenId);

                    if (orden == null)
                        return (false, "Orden no encontrada.");

                    if (orden.Estado == "Entregado")
                        return (false, "Esta orden ya fue entregada anteriormente.");

                    if (orden.SaldoPendiente > 0)
                    {
                        decimal montoRestante = orden.SaldoPendiente;
                        _db.Pagos.Add(new Pago
                        {
                            OrdenID = orden.OrdenID,
                            Monto = montoRestante,
                            FechaPago = DateTime.Now,
                            MetodoPago = "Efectivo",
                            Notas = "Pago al momento de recoger la ropa"
                        });

                        orden.MontoPagado += montoRestante;
                        orden.SaldoPendiente = 0;
                    }

                    orden.EstadoPago = "Pagado";
                    orden.Estado = "Entregado";
                    orden.EstadoRecojo = "Recogido";

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    return (true, (string?)null);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "Error al entregar orden {OrdenId}", ordenId);
                    return (false, _env.IsDevelopment() ? ex.GetBaseException().Message : "Ocurrió un error interno al entregar la orden.");
                }
            });
        }

        // ─────────────────────────────────────────────────────────────
        // OBTENER ORDEN CON DETALLES (para impresión)
        // ─────────────────────────────────────────────────────────────
        public async Task<Orden?> ObtenerOrdenConDetallesAsync(int ordenId)
        {
            return await _db.Ordenes
                .Include(o => o.Cliente)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.Servicio)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrdenID == ordenId);
        }

        // ─────────────────────────────────────────────────────────────
        // MÉTODOS PRIVADOS
        // ─────────────────────────────────────────────────────────────

        private async Task<Cliente> ObtenerOCrearClienteAsync(string? dni, string nombre, string apellidos, string? telefono)
        {
            var dniNormalizado = string.IsNullOrWhiteSpace(dni) ? null : new string(dni.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(dniNormalizado) && dniNormalizado.Length == 8)
            {
                var clientePorDni = await _db.Clientes.FirstOrDefaultAsync(c => c.Dni == dniNormalizado);
                if (clientePorDni != null)
                {
                    if (string.IsNullOrWhiteSpace(clientePorDni.Telefono) && !string.IsNullOrWhiteSpace(telefono))
                        clientePorDni.Telefono = telefono;

                    if (string.IsNullOrWhiteSpace(clientePorDni.Nombre) && !string.IsNullOrWhiteSpace(nombre))
                        clientePorDni.Nombre = nombre;

                    if (string.IsNullOrWhiteSpace(clientePorDni.Apellidos) && !string.IsNullOrWhiteSpace(apellidos))
                        clientePorDni.Apellidos = apellidos;

                    return clientePorDni;
                }
            }

            // ✅ CORREGIDO: búsqueda insensible a mayúsculas para evitar duplicados
            var nombreNorm = nombre.ToLower();
            var apellidosNorm = apellidos.ToLower();

            var clienteExistente = await _db.Clientes.FirstOrDefaultAsync(c =>
                c.Nombre.ToLower() == nombreNorm &&
                (c.Apellidos == null ? "" : c.Apellidos.ToLower()) == apellidosNorm);

            if (clienteExistente != null)
            {
                if (string.IsNullOrWhiteSpace(clienteExistente.Dni) && !string.IsNullOrWhiteSpace(dniNormalizado))
                    clienteExistente.Dni = dniNormalizado;

                return clienteExistente;
            }

            var nuevoCliente = new Cliente
            {
                Dni = dniNormalizado,
                Nombre = nombre,
                Apellidos = apellidos,
                Telefono = telefono
            };

            _db.Clientes.Add(nuevoCliente);
            await _db.SaveChangesAsync();
            return nuevoCliente;
        }

        private async Task<decimal> AgregarDetallesAsync(Orden orden, CrearOrdenRequest req)
        {
            decimal total = 0;

            for (int i = 0; i < req.ServicioIds.Count; i++)
            {
                var servicio = await _db.Servicios.FindAsync(req.ServicioIds[i]);
                if (servicio == null) continue;

                int cantidad = i < req.Cantidades.Count && req.Cantidades[i] > 0 ? req.Cantidades[i] : 0;
                decimal descuento = i < req.Descuentos.Count && req.Descuentos[i] > 0 ? req.Descuentos[i] : 0;

                if (cantidad <= 0) continue;

                decimal subtotal = servicio.Precio * cantidad;
                decimal totalDetalle = Math.Max(0, subtotal - descuento);

                _db.DetallesOrden.Add(new DetalleOrden
                {
                    OrdenID = orden.OrdenID,
                    ServicioID = servicio.ServicioID,
                    Cantidad = cantidad,
                    PrecioUnitario = servicio.Precio,
                    Descuento = descuento
                });

                total += totalDetalle;
            }

            await _db.SaveChangesAsync();
            return total;
        }

        private static string DeterminarEstadoPago(decimal montoPagado, decimal total)
        {
            if (montoPagado <= 0) return "Pendiente";
            if (montoPagado >= total) return "Pagado";
            return "Parcial";
        }
    }
}
