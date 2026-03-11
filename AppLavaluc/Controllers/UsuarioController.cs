using AppLavaluc.Data;
using AppLavaluc.Models;
using AppLavaluc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppLavaluc.Controllers
{
    [Authorize(Roles = "Admin")] // Solo los administradores pueden acceder
    public class UsuarioController : Controller
    {
        private readonly LavanderiaContext _context;

        public UsuarioController(LavanderiaContext context)
        {
            _context = context;
        }

        // GET: /Usuario
        public async Task<IActionResult> Index()
        {
            var usuarios = await _context.Usuarios.OrderBy(u => u.NombreCompleto).ToListAsync();
            return View(usuarios);
        }

        // GET: /Usuario/Crear
        public IActionResult Crear()
        {
            ViewBag.Roles = new SelectList(new[] { "Admin", "Empleado" });
            return View();
        }

        // POST: /Usuario/Crear
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(Usuario usuario, string password)
        {
            ModelState.Remove("PasswordHash");
            if (ModelState.IsValid)
            {
                if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
                {
                    ModelState.AddModelError("Password", "La contraseña es obligatoria y debe tener al menos 6 caracteres.");
                    ViewBag.Roles = new SelectList(new[] { "Admin", "Empleado" }, usuario.Rol);
                    return View(usuario);
                }

                usuario.PasswordHash = PasswordHelper.HashPassword(password);
                _context.Add(usuario);
                await _context.SaveChangesAsync();
                TempData["Mensaje"] = "Usuario creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Roles = new SelectList(new[] { "Admin", "Empleado" }, usuario.Rol);
            return View(usuario);
        }

        // GET: /Usuario/Editar/5
        public async Task<IActionResult> Editar(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario == null)
            {
                return NotFound();
            }
            ViewBag.Roles = new SelectList(new[] { "Admin", "Empleado" }, usuario.Rol);
            return View(usuario);
        }

        // POST: /Usuario/Editar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(int id, Usuario usuario, string? newPassword)
        {
            ModelState.Remove("PasswordHash");
            if (id != usuario.UsuarioID)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var userToUpdate = await _context.Usuarios.FindAsync(id);
                    if (userToUpdate == null) return NotFound();

                    userToUpdate.NombreCompleto = usuario.NombreCompleto;
                    userToUpdate.NombreUsuario = usuario.NombreUsuario;
                    userToUpdate.Email = usuario.Email;
                    userToUpdate.Rol = usuario.Rol;
                    userToUpdate.Activo = usuario.Activo;

                    if (!string.IsNullOrWhiteSpace(newPassword))
                    {
                        if (newPassword.Length < 6)
                        {
                            ModelState.AddModelError("Password", "La nueva contraseña debe tener al menos 6 caracteres.");
                            ViewBag.Roles = new SelectList(new[] { "Admin", "Empleado" }, usuario.Rol);
                            return View(usuario);
                        }
                        userToUpdate.PasswordHash = PasswordHelper.HashPassword(newPassword);
                    }

                    _context.Update(userToUpdate);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Usuarios.Any(e => e.UsuarioID == usuario.UsuarioID))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                TempData["Mensaje"] = "Usuario actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Roles = new SelectList(new[] { "Admin", "Empleado" }, usuario.Rol);
            return View(usuario);
        }
    }
}
