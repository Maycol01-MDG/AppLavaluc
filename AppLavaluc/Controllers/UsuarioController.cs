using AppLavaluc.Data;
using AppLavaluc.Models;
using AppLavaluc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsuarioController : Controller
    {
        private readonly LavanderiaContext _context;
        private readonly ILogger<UsuarioController> _logger;

        private static readonly string[] Roles = { "Admin", "Empleado" };

        public UsuarioController(LavanderiaContext context, ILogger<UsuarioController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var usuarios = await _context.Usuarios
                .OrderBy(u => u.NombreCompleto)
                .ToListAsync();

            return View(usuarios);
        }

        public IActionResult Crear()
        {
            ViewBag.Roles = new SelectList(Roles);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(Usuario usuario, string password)
        {
            ModelState.Remove("PasswordHash");

            if (!ModelState.IsValid)
            {
                ViewBag.Roles = new SelectList(Roles, usuario.Rol);
                return View(usuario);
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            {
                ModelState.AddModelError("Password", "La contraseña debe tener al menos 6 caracteres.");
                ViewBag.Roles = new SelectList(Roles, usuario.Rol);
                return View(usuario);
            }

            try
            {
                usuario.PasswordHash = PasswordHelper.HashPassword(password);
                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();
                TempData["Mensaje"] = "✅ Usuario creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear usuario");
                TempData["Error"] = "Error al guardar el usuario.";
                ViewBag.Roles = new SelectList(Roles, usuario.Rol);
                return View(usuario);
            }
        }

        public async Task<IActionResult> Editar(int? id)
        {
            if (id == null) return NotFound();

            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario == null) return NotFound();

            ViewBag.Roles = new SelectList(Roles, usuario.Rol);
            return View(usuario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(int id, Usuario usuario, string? newPassword)
        {
            ModelState.Remove("PasswordHash");

            if (id != usuario.UsuarioID) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Roles = new SelectList(Roles, usuario.Rol);
                return View(usuario);
            }

            // Validar nueva contraseña si se proporcionó
            if (!string.IsNullOrWhiteSpace(newPassword) && newPassword.Length < 6)
            {
                ModelState.AddModelError("Password", "La nueva contraseña debe tener al menos 6 caracteres.");
                ViewBag.Roles = new SelectList(Roles, usuario.Rol);
                return View(usuario);
            }

            try
            {
                var usuarioExistente = await _context.Usuarios.FindAsync(id);
                if (usuarioExistente == null) return NotFound();

                usuarioExistente.NombreCompleto = usuario.NombreCompleto;
                usuarioExistente.NombreUsuario = usuario.NombreUsuario;
                usuarioExistente.Email = usuario.Email;
                usuarioExistente.Rol = usuario.Rol;
                usuarioExistente.Activo = usuario.Activo;

                if (!string.IsNullOrWhiteSpace(newPassword))
                    usuarioExistente.PasswordHash = PasswordHelper.HashPassword(newPassword);

                _context.Usuarios.Update(usuarioExistente);
                await _context.SaveChangesAsync();

                TempData["Mensaje"] = "✅ Usuario actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Usuarios.AnyAsync(u => u.UsuarioID == id))
                    return NotFound();
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al editar usuario {Id}", id);
                TempData["Error"] = "Error al actualizar el usuario.";
                ViewBag.Roles = new SelectList(Roles, usuario.Rol);
                return View(usuario);
            }
        }
    }
}