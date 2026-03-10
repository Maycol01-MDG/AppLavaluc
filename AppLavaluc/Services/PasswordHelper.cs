namespace AppLavaluc.Services
{
    public static class PasswordHelper
    {
        // Genera el hash de una contraseña
        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        // Verifica si una contraseña coincide con un hash
        public static bool VerifyPassword(string password, string passwordHash)
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
    }
}
