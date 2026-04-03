using AppLavaluc.Data;
using AppLavaluc.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AppLavaluc.Controllers
{
    public class CuentaController : Controller
    {
        private readonly LavanderiaContext _context;
        private readonly ILogger<CuentaController> _logger;

        public CuentaController(LavanderiaContext context, ILogger<CuentaController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login()
        {
            // Si ya está autenticado, redirigir al inicio
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                TempData["Error"] = "Usuario y contraseña son obligatorios.";
                return View();
            }

            var credencial = username.Trim().ToLower();

            // ✅ Búsqueda normalizada insensible a mayúsculas
            var usuario = _context.Usuarios.FirstOrDefault(u =>
                u.NombreUsuario.ToLower() == credencial ||
                u.Email.ToLower() == credencial);

            // Verificar credenciales
            if (usuario == null ||
                string.IsNullOrEmpty(usuario.PasswordHash) ||
                !PasswordHelper.VerifyPassword(password, usuario.PasswordHash))
            {
                _logger.LogWarning("Intento de login fallido para usuario: {Username}", username);
                TempData["Error"] = "Usuario/correo o contraseña incorrectos.";
                return View();
            }

            if (!usuario.Activo)
            {
                TempData["Error"] = "Tu cuenta está desactivada. Contacta al administrador.";
                return View();
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name,           usuario.NombreUsuario),
                new Claim(ClaimTypes.NameIdentifier, usuario.UsuarioID.ToString()),
                new Claim(ClaimTypes.Role,           usuario.Rol)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var properties = new AuthenticationProperties { IsPersistent = true };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                properties);

            _logger.LogInformation("Login exitoso para usuario: {Username}", usuario.NombreUsuario);
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }
    }
}