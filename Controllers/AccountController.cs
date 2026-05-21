using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TicketSistemi.Data;
using TicketSistemi.Models;
using TicketSistemi.Utils;

namespace TicketSistemi.Controllers
{
    public class AccountController : Controller
    {
        // 1. Login Sayfasını Göster (GET)
        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Ticket");
            }
            return View();
        }

        // 2. Giriş İsteklerini Karşıla (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password)
        {
            var users = JsonDbManager.GetUsers();
            var user = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

            if (user != null && PasswordHelper.VerifyPassword(user.Username, user.PasswordHash, password))
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                return RedirectToAction("Index", "Ticket");
            }

            ViewBag.ErrorMessage = "Kullanıcı adı veya şifre hatalı!";
            return View();
        }

        // 3. Kayıt Sayfasını Göster (GET)
        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Ticket");
            }
            return View();
        }

        // 4. Kayıt İsteklerini Karşıla (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(string username, string password, string confirmPassword)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.ErrorMessage = "Kullanıcı adı ve şifre alanları zorunludur!";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.ErrorMessage = "Şifreler uyuşmuyor!";
                return View();
            }

            var users = JsonDbManager.GetUsers();
            if (users.Any(u => string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                ViewBag.ErrorMessage = "Bu kullanıcı adı zaten alınmış!";
                return View();
            }

            var newUser = new User
            {
                Id = users.Any() ? users.Max(u => u.Id) + 1 : 1,
                Username = username.Trim(),
                PasswordHash = PasswordHelper.HashPassword(username.Trim(), password),
                Role = "User" // Yeni kayıt olanlar varsayılan olarak "User" (Müşteri) olur.
            };

            users.Add(newUser);
            JsonDbManager.SaveUsers(users);

            TempData["SuccessMessage"] = "Kayıt başarıyla tamamlandı! Şimdi giriş yapabilirsiniz.";
            return RedirectToAction("Login");
        }

        // 5. Çıkış Yap (POST veya GET)
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Ticket");
        }
    }
}