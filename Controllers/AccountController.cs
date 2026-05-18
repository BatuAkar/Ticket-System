using Microsoft.AspNetCore.Mvc;

namespace TicketSistemi.Controllers
{
    public class AccountController : Controller
    {
        // 1. Login Sayfasını Göster (GET)
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // 2. Giriş İsteklerini Karşıla (POST)
        [HttpPost]
        public IActionResult Login(string username, string password)
        {
            // Basitçe statik kullanıcı kontrolü yapıyoruz (Veritabanı yükünden kurtulmak için)
            if (username == "admin" && password == "123")
            {
                // Tarayıcıya "Ben Adminim" çerezi (Cookie) bırakıyoruz
                Response.Cookies.Append("UserRole", "Admin", new CookieOptions { Expires = DateTimeOffset.Now.AddMinutes(30) });
                return RedirectToAction("Index", "Ticket");
            }
            else if (username == "user" && password == "123")
            {
                // Tarayıcıya "Ben Normal Kullanıcıyım" çerezi bırakıyoruz
                Response.Cookies.Append("UserRole", "User", new CookieOptions { Expires = DateTimeOffset.Now.AddMinutes(30) });
                return RedirectToAction("Index", "Ticket");
            }

            // Giriş hatalıysa mesaj gönder
            ViewBag.ErrorMessage = "Kullanıcı adı veya şifre hatalı!";
            return View();
        }

        // 3. Çıkış Yap (POST veya GET)
        public IActionResult Logout()
        {
            // Tarayıcıdaki rol çerezini siliyoruz
            Response.Cookies.Delete("UserRole");
            return RedirectToAction("Index", "Ticket");
        }
    }
}