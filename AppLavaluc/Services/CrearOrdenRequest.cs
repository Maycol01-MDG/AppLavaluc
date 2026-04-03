namespace AppLavaluc.Services
{
    /// <summary>
    /// Objeto de transferencia para crear una orden.
    /// Separa los datos del formulario del modelo de base de datos.
    /// </summary>
    public class CrearOrdenRequest
    {
        public string NombreCliente { get; set; } = string.Empty;
        public string ApellidosCliente { get; set; } = string.Empty;
        public string? TelefonoCliente { get; set; }
        public string TipoEntrega { get; set; } = string.Empty;
        public decimal MontoPagado { get; set; }
        public DateTime? FechaEntregaEstimada { get; set; }
        public string? Observaciones { get; set; }

        public List<int> ServicioIds { get; set; } = new();
        public List<int> Cantidades { get; set; } = new();
        public List<decimal> Descuentos { get; set; } = new();
    }
}