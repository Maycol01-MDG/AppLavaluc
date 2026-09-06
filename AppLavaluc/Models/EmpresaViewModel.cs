using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace AppLavaluc.Models
{
    public class EmpresaViewModel
    {
        [Required(ErrorMessage = "El RUC es obligatorio")]
        [StringLength(11, MinimumLength = 11, ErrorMessage = "El RUC debe tener 11 dígitos")]
        [Display(Name = "RUC")]
        public string Ruc { get; set; } = string.Empty;

        [Required(ErrorMessage = "La Razón Social es obligatoria")]
        [Display(Name = "Razón Social")]
        public string RazonSocial { get; set; } = string.Empty;

        [Display(Name = "Nombre Comercial")]
        public string? NombreComercial { get; set; }

        [Required(ErrorMessage = "La Dirección es obligatoria")]
        [Display(Name = "Dirección Fiscal")]
        public string Direccion { get; set; } = string.Empty;

        [Display(Name = "Código Ubigeo")]
        public string? Ubigeo { get; set; } = "150101";

        [Display(Name = "Departamento")]
        public string? Departamento { get; set; } = "LIMA";

        [Display(Name = "Provincia")]
        public string? Provincia { get; set; } = "LIMA";

        [Display(Name = "Distrito")]
        public string? Distrito { get; set; } = "LIMA";

        [Display(Name = "Urbanización")]
        public string? Urbanizacion { get; set; } = "-";

        [Display(Name = "Código Local SUNAT")]
        public string? CodLocal { get; set; } = "0000";

        [Display(Name = "Usuario SOL SUNAT")]
        public string? SolUser { get; set; } = "prueba";

        [Display(Name = "Clave SOL SUNAT")]
        public string? SolPass { get; set; } = "prueba";

        [Display(Name = "Logo de la Empresa")]
        public IFormFile? LogoFile { get; set; }

        public string? LogoUrl { get; set; }
        public bool TieneLogo { get; set; }
    }
}
