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

            if (user == null || !PasswordHelper.VerifyPassword(password, user.PasswordHash))
            {
                TempData["Error"] = "Credenciales inválidas.";
                return View();
            }

            if (!user.Activo)
            {
                TempData["Error"] = "Su cuenta está desactivada. Contacte al administrador.";
                return View();
            }

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
                new AuthenticationProperties { IsPersistent = true }); // IsPersistent = true para recordar la sesión

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
