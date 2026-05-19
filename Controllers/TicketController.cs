using Microsoft.AspNetCore.Mvc;
using TicketSistemi.Models;
using TicketSistemi.Data;
using System.Linq;

namespace TicketSistemi.Controllers
{
    public class TicketController : Controller
    {
        // 1. Tüm Ticket'ları Listeleme Ekranı
        public IActionResult Index(TicketStatus? status)
        {
            var userRole = Request.Cookies["UserRole"];
            var username = Request.Cookies["Username"];

            if (string.IsNullOrEmpty(userRole) || string.IsNullOrEmpty(username))
            {
                return RedirectToAction("Login", "Account");
            }

            var tickets = JsonDbManager.GetTickets();

            // Eğer normal kullanıcı ise sadece kendi biletlerini süzüyoruz
            if (userRole == "User")
            {
                tickets = tickets.Where(t => t.CustomerName == username).ToList();
            }

            // Sayımları filtrelemeden bağımsız olarak, yetki sınırları dahilinde hesaplayıp ViewBag'e atıyoruz
            ViewBag.TotalCount = tickets.Count;
            ViewBag.OpenCount = tickets.Count(t => t.Status == TicketStatus.Acik);
            ViewBag.SolvedCount = tickets.Count(t => t.Status == TicketStatus.Cozuldu);

            // LINQ ile filtreleme (Eğer bir durum seçilmişse)
            if (status.HasValue)
            {
                tickets = tickets.Where(t => t.Status == status.Value).ToList();
            }

            // Tarihe göre ters sıralıyoruz.
            var sortedTickets = tickets.OrderByDescending(t => t.CreatedDate).ToList();
            
            // Seçili filtreyi View'a taşıyoruz
            ViewBag.CurrentStatus = status;
            
            return View(sortedTickets);
        }

        // 2. Yeni Ticket Oluşturma Ekranı (GET)
        [HttpGet]
        public IActionResult Create()
        {
            var userRole = Request.Cookies["UserRole"];
            if (string.IsNullOrEmpty(userRole))
            {
                return RedirectToAction("Login", "Account");
            }
            return View();
        }

        // 3. Form Doldurulup Gönderildiğinde Çalışacak Kısım (POST)
        [HttpPost]
        public IActionResult Create(Ticket newTicket)
        {
            var userRole = Request.Cookies["UserRole"];
            var username = Request.Cookies["Username"];
            if (string.IsNullOrEmpty(userRole) || string.IsNullOrEmpty(username))
            {
                return RedirectToAction("Login", "Account");
            }

            // Müşteri adını oturum çerezinden otomatik atıyoruz (Güvenlik için)
            newTicket.CustomerName = username;
            ModelState.Remove("CustomerName");

            if (ModelState.IsValid)
            {
                var tickets = JsonDbManager.GetTickets();
                
                newTicket.Id = tickets.Any() ? tickets.Max(t => t.Id) + 1 : 1;
                
                tickets.Add(newTicket);
                JsonDbManager.SaveTickets(tickets);
                
                return RedirectToAction("Index");
            }

            return View(newTicket);
        }

        // 4. Admin Cevaplama Ekranı (GET)
        [HttpGet]
        public IActionResult Reply(int id)
        {
            if (Request.Cookies["UserRole"] != "Admin")
{
    return RedirectToAction("Login", "Account");
}
            var tickets = JsonDbManager.GetTickets();
            var ticket = tickets.FirstOrDefault(t => t.Id == id);
            
            if (ticket == null) return NotFound();
            
            return View(ticket);
        }

        // 5. Admin Cevabı Kaydettiğinde (POST)
        [HttpPost]
        public IActionResult Reply(int id, string supportReply, TicketStatus status)
        {
            if (Request.Cookies["UserRole"] != "Admin")
            {
                return RedirectToAction("Login", "Account");
            }
            var tickets = JsonDbManager.GetTickets();
            var ticketIndex = tickets.FindIndex(t => t.Id == id);
            
            if (ticketIndex == -1) return NotFound();
            
            tickets[ticketIndex].SupportReply = supportReply;
            tickets[ticketIndex].Status = status;
            
            // Eğer daha önce üstlenilmemişse, otomatik olarak cevaplayan admini ata
            if (string.IsNullOrEmpty(tickets[ticketIndex].AssignedAgent))
            {
                tickets[ticketIndex].AssignedAgent = Request.Cookies["Username"] ?? "Destek Elemanı";
            }
            
            JsonDbManager.SaveTickets(tickets);
            
            return RedirectToAction("Index");
        }

        // 6. Talebi Üstlenme (POST)
        [HttpPost]
        public IActionResult Claim(int id)
        {
            if (Request.Cookies["UserRole"] != "Admin")
            {
                return RedirectToAction("Login", "Account");
            }
            var tickets = JsonDbManager.GetTickets();
            var ticketIndex = tickets.FindIndex(t => t.Id == id);
            
            if (ticketIndex == -1) return NotFound();
            
            tickets[ticketIndex].AssignedAgent = Request.Cookies["Username"] ?? "Destek Elemanı";
            JsonDbManager.SaveTickets(tickets);
            
            return RedirectToAction("Index");
        }

        // 7. Talebi Silme (POST)
        [HttpPost]
        public IActionResult Delete(int id)
        {
            if (Request.Cookies["UserRole"] != "Admin")
            {
                return RedirectToAction("Login", "Account");
            }
            var tickets = JsonDbManager.GetTickets();
            var ticket = tickets.FirstOrDefault(t => t.Id == id);
            
            if (ticket == null) return NotFound();
            
            tickets.Remove(ticket);
            JsonDbManager.SaveTickets(tickets);
            
            return RedirectToAction("Index");
        }
    }
}