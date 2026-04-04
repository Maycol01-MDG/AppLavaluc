using AppLavaluc.Data;
using AppLavaluc.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────
// SERVICIOS
// ─────────────────────────────────────────────────────────────

// Base de datos MySQL con Pomelo
var connectionStringName = builder.Environment.IsDevelopment()
    ? "LavalucContextLocal"
    : "LavalucContext";

builder.Services.AddDbContext<LavanderiaContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString(connectionStringName) ??
        builder.Configuration.GetConnectionString("LavalucContext"),
        new MySqlServerVersion(new Version(8, 0, 40)),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)
    )
);

builder.Services.AddControllersWithViews();

// ✅ Impresora térmica: Singleton porque es un recurso de hardware compartido
builder.Services.AddSingleton<EscPosTicketPrinter>();

// ✅ NUEVO: registrar el servicio de órdenes con su interfaz
builder.Services.AddScoped<IOrdenService, OrdenService>();

// Autenticación por cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Cuenta/Login";
        options.LogoutPath = "/Cuenta/Logout";
        options.AccessDeniedPath = "/Home/Error";
        options.ExpireTimeSpan = TimeSpan.FromHours(8); // Sesión de 8 horas
        options.SlidingExpiration = true;
    });

var app = builder.Build();

// ─────────────────────────────────────────────────────────────
// PIPELINE HTTP
// ─────────────────────────────────────────────────────────────

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ─────────────────────────────────────────────────────────────
// SEED DE DATOS: usuario administrador inicial
// ─────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        var context = services.GetRequiredService<LavanderiaContext>();
        var migrateOnStartup = app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Database:MigrateOnStartup");
        var seedOnStartup = app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Database:SeedOnStartup");

        if (migrateOnStartup)
            context.Database.Migrate();

        if (seedOnStartup)
        {
            bool adminExiste = context.Usuarios.Any(u =>
                u.Email.ToLower() == "admin@lavaluc.com" ||
                u.NombreUsuario.ToLower() == "admin");

            if (!adminExiste)
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
                logger.LogInformation("Usuario administrador creado correctamente.");
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error durante el seed de datos al iniciar la aplicación.");
        Console.WriteLine("ERROR FATAL EN SEED: " + ex.Message);
        if (ex.InnerException != null)
            Console.WriteLine("DETALLE: " + ex.InnerException.Message);
    }
}

app.Run();
