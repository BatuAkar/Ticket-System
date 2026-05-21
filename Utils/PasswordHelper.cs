using Microsoft.AspNetCore.Identity;

namespace TicketSistemi.Utils
{
    public static class PasswordHelper
    {
        private static readonly PasswordHasher<string> _hasher = new PasswordHasher<string>();

        public static string HashPassword(string username, string password)
        {
            return _hasher.HashPassword(username, password);
        }

        public static bool VerifyPassword(string username, string hashedPassword, string providedPassword)
        {
            var result = _hasher.VerifyHashedPassword(username, hashedPassword, providedPassword);
            return result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded;
        }
    }
}
