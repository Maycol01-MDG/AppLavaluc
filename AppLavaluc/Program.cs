using AppLavaluc.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Configuración de MySQL con Pomelo
builder.Services.AddDbContext<LavanderiaContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("LavalucContext"),
        new MySqlServerVersion(new Version(8, 0, 40))
    )
);

builder.Services.AddControllersWithViews();

// Configuración de Autenticación por Cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Cuenta/Login";
        options.LogoutPath = "/Cuenta/Logout";
        options.AccessDeniedPath = "/Home/AccessDenied"; // Página para accesos no autorizados
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication(); // Habilita la autenticación
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

/// Seed de datos para el usuario inicial
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<LavanderiaContext>();

        // 1. ESTO OBLIGA A ENTITY FRAMEWORK A CREAR LA BASE DE DATOS Y LAS TABLAS SI NO EXISTEN
        context.Database.EnsureCreated();

        var adminUser = context.Usuarios.FirstOrDefault(u =>
            u.Email.ToLower() == "admin@lavaluc.com" ||
            u.NombreUsuario.ToLower() == "admin");

        if (adminUser == null)
        {
            context.Usuarios.Add(new AppLavaluc.Models.Usuario
            {
                NombreUsuario = "admin",
                NombreCompleto = "Administrador",
                Email = "admin@lavaluc.com",
                PasswordHash = AppLavaluc.Services.PasswordHelper.HashPassword("123456"),
                Rol = "Admin",
                Activo = true
            });
            context.SaveChanges();
            Console.WriteLine("¡ÉXITO! El usuario administrador fue creado en la base de datos.");
        }
    }
    catch (Exception ex)
    {
        // 2. ESTO IMPRIMIRÁ EL ERROR REAL DE MYSQL EN TU CONSOLA NEGRA
        Console.WriteLine("==================================================");
        Console.WriteLine("ERROR FATAL AL CREAR EL USUARIO EN MYSQL:");
        Console.WriteLine(ex.Message);
        if (ex.InnerException != null)
        {
            Console.WriteLine("DETALLE DEL ERROR: " + ex.InnerException.Message);
        }
        Console.WriteLine("==================================================");

        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Ocurrió un error durante el sembrado de datos.");
    }
}

app.Run();
