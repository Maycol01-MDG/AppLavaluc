using AppLavaluc.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configuración de MySQL con Pomelo
builder.Services.AddDbContext<LavanderiaContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("LavalucContext"),
        new MySqlServerVersion(new Version(8, 0, 40))
    )
);

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.Run();
