using AppLavaluc.Data;
using AppLavaluc.Models;
using AppLavaluc.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AppLavaluc.Controllers
{
    public class CuentaController : Controller
    {
        private readonly LavanderiaContext _context;

        public CuentaController(LavanderiaContext context)
        {
            _context = context;
        }

        // GET: /Cuenta/Login
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // POST: /Cuenta/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                TempData["Error"] = "Usuario y contraseña son obligatorios.";
                return View();
            }

            var user = _context.Usuarios.FirstOrDefault(u => u.NombreUsuario == username || u.Email == username);

            // DIAGNÓSTICO 1: ¿El usuario realmente existe?
            if (user == null)
            {
                TempData["Error"] = $"DIAGNÓSTICO 1: El correo '{username}' NO existe en la base de datos de MySQL.";
                return View();
            }

            // DIAGNÓSTICO 2: ¿El Hash es correcto?
            bool passwordCorrecta = PasswordHelper.VerifyPassword(password, user.PasswordHash);
            if (!passwordCorrecta)
            {
                // Esto nos mostrará qué hay realmente guardado en tu BD
                TempData["Error"] = $"DIAGNÓSTICO 2: Contraseña incorrecta. El hash guardado en tu BD es: '{user.PasswordHash}'";
                return View();
            }

            // DIAGNÓSTICO 3: ¿El usuario está activo?
            if (!user.Activo)
            {
                TempData["Error"] = "DIAGNÓSTICO 3: El usuario existe y la contraseña es correcta, pero la cuenta está desactivada (Activo = false).";
                return View();
            }

            // Si pasa todas las pruebas, inicia sesión
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.NombreUsuario),
        new Claim(ClaimTypes.NameIdentifier, user.UsuarioID.ToString()),
        new Claim(ClaimTypes.Role, user.Rol)
    };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                new AuthenticationProperties { IsPersistent = true });

            return RedirectToAction("Index", "Home");
        }

        // POST: /Cuenta/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Cuenta");
        }
    }
}