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

// ✅ NUEVO: registrar el servicio de facturación electrónica
builder.Services.AddScoped<IFacturacionService, FacturacionService>();

// ✅ NUEVO: registrar el servicio de consulta unificada de documentos (DNI / RUC Latinfo)
builder.Services.AddHttpClient<IConsultaDocumentoService, ConsultaDocumentoService>();

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

        try
        {
            var count = context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Clientes' AND COLUMN_NAME = 'Dni'"
            ).AsEnumerable().FirstOrDefault();
            if (count == 0)
            {
                context.Database.ExecuteSqlRaw("ALTER TABLE `Clientes` ADD COLUMN `Dni` varchar(11) NULL;");
            }
            else
            {
                context.Database.ExecuteSqlRaw("ALTER TABLE `Clientes` MODIFY COLUMN `Dni` varchar(11) NULL;");
            }

            var countDir = context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Clientes' AND COLUMN_NAME = 'Direccion'"
            ).AsEnumerable().FirstOrDefault();
            if (countDir == 0)
            {
                context.Database.ExecuteSqlRaw("ALTER TABLE `Clientes` ADD COLUMN `Direccion` varchar(250) NULL;");
            }
        }
        catch (Exception exDni)
        {
            logger.LogWarning("Comprobación de columnas Clientes (Dni/Dirección): {Msg}", exDni.Message);
        }

        try
        {
            context.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS `Comprobantes` (
                  `ComprobanteID` int NOT NULL AUTO_INCREMENT,
                  `OrdenID` int NOT NULL,
                  `TipoDoc` varchar(2) NOT NULL,
                  `Serie` varchar(10) NOT NULL,
                  `Correlativo` int NOT NULL,
                  `FechaEmision` datetime(6) NOT NULL,
                  `RucEmisor` varchar(11) NULL,
                  `RazonSocialEmisor` varchar(200) NULL,
                  `TipoDocCliente` varchar(2) NOT NULL,
                  `NumDocCliente` varchar(20) NULL,
                  `RznSocialCliente` varchar(200) NULL,
                  `DireccionCliente` varchar(300) NULL,
                  `MontoOperacionesGravadas` decimal(10,2) NOT NULL,
                  `MontoIgv` decimal(10,2) NOT NULL,
                  `MontoTotal` decimal(10,2) NOT NULL,
                  `EstadoSunat` varchar(50) NOT NULL DEFAULT 'Pendiente',
                  `CodigoRespuesta` varchar(20) NULL,
                  `MensajeSunat` varchar(500) NULL,
                  `HashCdr` varchar(200) NULL,
                  `NombreArchivo` varchar(100) NULL,
                  `XmlFirmado` longtext NULL,
                  `TipDocAfectado` varchar(2) NULL,
                  `NumDocAfectado` varchar(30) NULL,
                  `CodMotivo` varchar(10) NULL,
                  `DesMotivo` varchar(250) NULL,
                  `ComprobanteReferenciaID` int NULL,
                  PRIMARY KEY (`ComprobanteID`),
                  KEY `IX_Comprobantes_OrdenID` (`OrdenID`),
                  CONSTRAINT `FK_Comprobantes_Ordenes_OrdenID` FOREIGN KEY (`OrdenID`) REFERENCES `Ordenes` (`OrdenID`) ON DELETE CASCADE
                );
            ");

            // Asegurar que las nuevas columnas existan si la tabla ya había sido creada antes
            string[,] columnasNc = {
                { "TipDocAfectado", "varchar(2)" },
                { "NumDocAfectado", "varchar(30)" },
                { "CodMotivo", "varchar(10)" },
                { "DesMotivo", "varchar(250)" },
                { "ComprobanteReferenciaID", "int" }
            };
            for (int i = 0; i < columnasNc.GetLength(0); i++)
            {
                var colNombre = columnasNc[i, 0];
                var countCol = context.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Comprobantes' AND COLUMN_NAME = {0}",
                    colNombre
                ).AsEnumerable().FirstOrDefault();

                if (countCol == 0)
                {
                    if (colNombre == "TipDocAfectado") context.Database.ExecuteSqlRaw("ALTER TABLE `Comprobantes` ADD COLUMN `TipDocAfectado` varchar(2) NULL;");
                    else if (colNombre == "NumDocAfectado") context.Database.ExecuteSqlRaw("ALTER TABLE `Comprobantes` ADD COLUMN `NumDocAfectado` varchar(30) NULL;");
                    else if (colNombre == "CodMotivo") context.Database.ExecuteSqlRaw("ALTER TABLE `Comprobantes` ADD COLUMN `CodMotivo` varchar(10) NULL;");
                    else if (colNombre == "DesMotivo") context.Database.ExecuteSqlRaw("ALTER TABLE `Comprobantes` ADD COLUMN `DesMotivo` varchar(250) NULL;");
                    else if (colNombre == "ComprobanteReferenciaID") context.Database.ExecuteSqlRaw("ALTER TABLE `Comprobantes` ADD COLUMN `ComprobanteReferenciaID` int NULL;");
                }
            }
        }
        catch (Exception exComp)
        {
            logger.LogWarning("Comprobación de tabla Comprobantes: {Msg}", exComp.Message);
        }

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
