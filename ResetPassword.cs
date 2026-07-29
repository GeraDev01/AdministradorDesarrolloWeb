using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Security;

// Este archivo permite resetear la contraseña del admin ejecutando:
// dotnet run --project ResetPassword.csproj

var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdministradorDesarrolloWeb");
var dbPath = Path.Combine(appDataPath, "app.db");

Console.WriteLine($"Base de datos: {dbPath}");

if (!File.Exists(dbPath))
{
    Console.WriteLine("❌ Base de datos no encontrada. Asegúrate de que la aplicación se haya ejecutado al menos una vez.");
    return 1;
}

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;

using var db = new AppDbContext(options);

// Buscar usuario admin
var admin = db.Users.FirstOrDefault(u => u.Username == "admin");

if (admin == null)
{
    // Crear nuevo admin
    admin = new()
    {
        Username = "admin",
        FullName = "Administrador",
        PasswordHash = PasswordHasher.Hash("Admin@123"),
        Role = Administrador_Desarrollo_Web.Models.UserRole.Admin,
        IsActive = true,
        MustChangePassword = true,
        CreatedAt = DateTime.UtcNow
    };
    db.Users.Add(admin);
    Console.WriteLine("✅ Usuario admin creado.");
}
else
{
    // Resetear contraseña
    admin.PasswordHash = PasswordHasher.Hash("Admin@123");
    admin.MustChangePassword = true;
    Console.WriteLine("✅ Contraseña del admin restablecida.");
}

db.SaveChanges();
Console.WriteLine("\n📝 Credenciales:");
Console.WriteLine("   Usuario: admin");
Console.WriteLine("   Contraseña: Admin@123");
Console.WriteLine("\n⚠️  Te pedirá cambiar la contraseña al iniciar sesión.");
return 0;
