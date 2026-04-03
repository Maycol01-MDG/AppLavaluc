using AppLavaluc.Models;

namespace AppLavaluc.Services
{
    /// <summary>
    /// Contrato del servicio de órdenes.
    /// Toda la lógica de negocio vive aquí, NO en el controlador.
    /// </summary>
    public interface IOrdenService
    {
        Task<(bool Ok, int OrdenId, string? Error)> CrearOrdenAsync(CrearOrdenRequest request);
        Task<(bool Ok, string? Error)> EntregarOrdenAsync(int ordenId);
        Task<Orden?> ObtenerOrdenConDetallesAsync(int ordenId);
    }
}