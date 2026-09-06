using AppLavaluc.Models;

namespace AppLavaluc.Services
{
    public interface IConsultaDocumentoService
    {
        /// <summary>
        /// Realiza la consulta unificada de un documento (DNI de 8 dígitos o RUC de 11 dígitos).
        /// Busca primero en la BD local, luego en proveedores oficiales (RENIEC / SUNAT-Latinfo).
        /// </summary>
        Task<ConsultaDocumentoRespuesta> ConsultarAsync(string numeroDocumento);
    }
}
