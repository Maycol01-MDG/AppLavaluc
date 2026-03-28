using System;

namespace AppLavaluc.Services
{
    public static class PasswordHelper
    {
        // Genera el hash de una contraseña
        public static string HashPassword(string password)
        {
            // El método de BCrypt ya genera e incluye el "Salt" automáticamente
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        // Verifica si una contraseña coincide con un hash
        public static bool VerifyPassword(string password, string passwordHash)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, passwordHash);
            }
            catch (Exception)
            {
                // Si el hash antiguo no es formato BCrypt, capturamos el error para que no se caiga la app
                return false;
            }
        }
    }
}