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

bool IsUsableConnectionString(string? value) =>
    !string.IsNullOrWhiteSpace(value) &&
    !value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

string? ResolveConnectionString()
{
    var configCandidates = builder.Environment.IsDevelopment()
        ? new[] { "LavalucContextLocal", "LavalucContext" }
        : new[] { "LavalucContext", "LavalucContextLocal" };

    foreach (var key in configCandidates)
    {
        var candidate = builder.Configuration.GetConnectionString(key);
        if (IsUsableConnectionString(candidate))
            return candidate;
    }

    var envCandidates = builder.Environment.IsDevelopment()
        ? new[] { "ConnectionStrings__LavalucContextLocal", "LAVALUC_CONTEXT_LOCAL", "MYSQLCONNSTR_LavalucContextLocal", "MYSQLCONNSTR_LavalucContext" }
        : new[] { "ConnectionStrings__LavalucContext", "LAVALUC_CONTEXT", "MYSQLCONNSTR_LavalucContext", "MYSQLCONNSTR_LavalucContextLocal" };

    foreach (var key in envCandidates)
    {
        var candidate = Environment.GetEnvironmentVariable(key);
        if (IsUsableConnectionString(candidate))
            return candidate;
    }

    return null;
}

var connectionString = ResolveConnectionString();

if (!IsUsableConnectionString(connectionString))
    throw new InvalidOperationException($"No se encontró una cadena de conexión válida para '{connectionStringName}'. Configura ConnectionStrings o una variable de entorno.");

builder.Services.AddDbContext<LavanderiaContext>(options =>
    options.UseMySql(
        connectionString,
        new MySqlServerVersion(new Version(8, 0, 40)),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)
    )
);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

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
        context.Database.ExecuteSqlRaw("ALTER TABLE `Clientes` ADD COLUMN IF NOT EXISTS `Dni` varchar(8) NULL;");
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
