using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSistemi.Models;
using TicketSistemi.Data;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using TicketSistemi.Hubs;

namespace TicketSistemi.Controllers
{
    [Authorize]
    public class TicketController : Controller
    {
        private readonly IHubContext<NotificationHub> _hubContext;

        public TicketController(IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        // 1. Tüm Ticket'ları Listeleme Ekranı
        public IActionResult Index(TicketStatus? status)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("Login", "Account");
            }

            var isAdmin = User.IsInRole("Admin");
            var tickets = JsonDbManager.GetTickets();

            // Eğer normal kullanıcı ise sadece kendi biletlerini süzüyoruz
            if (!isAdmin)
            {
                tickets = tickets.Where(t => t.CustomerName == username).ToList();
            }

            // Sayımları filtrelemeden bağımsız olarak, yetki sınırları dahilinde hesaplayıp ViewBag'e atıyoruz
            ViewBag.TotalCount = tickets.Count;
            ViewBag.OpenCount = tickets.Count(t => t.Status == TicketStatus.Acik);
            ViewBag.SolvedCount = tickets.Count(t => t.Status == TicketStatus.Cozuldu);
            ViewBag.ClosedCount = tickets.Count(t => t.Status == TicketStatus.Kapandi);

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
            return View();
        }

        // 3. Form Doldurulup Gönderildiğinde Çalışacak Kısım (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Ticket newTicket)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
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

                // SignalR Live Notification
                _hubContext.Clients.All.SendAsync("ReceiveNotification", $"Yeni bir destek talebi oluşturuldu! Konu: {newTicket.Title}", "Admin");
                
                return RedirectToAction("Index");
            }

            return View(newTicket);
        }

        // 4. Admin Cevaplama Ekranı (GET)
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult Reply(int id)
        {
            var tickets = JsonDbManager.GetTickets();
            var ticket = tickets.FirstOrDefault(t => t.Id == id);
            
            if (ticket == null) return NotFound();
            
            return View(ticket);
        }

        // 5. Admin Cevabı Kaydettiğinde (POST)
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public IActionResult Reply(int id, string supportReply, TicketStatus status)
        {
            var tickets = JsonDbManager.GetTickets();
            var ticketIndex = tickets.FindIndex(t => t.Id == id);
            
            if (ticketIndex == -1) return NotFound();
            
            tickets[ticketIndex].SupportReply = supportReply;
            tickets[ticketIndex].Status = status;
            
            // Eğer daha önce üstlenilmemişse, otomatik olarak cevaplayan admini ata
            if (string.IsNullOrEmpty(tickets[ticketIndex].AssignedAgent))
            {
                tickets[ticketIndex].AssignedAgent = User.Identity?.Name ?? "Destek Elemanı";
            }
            
            JsonDbManager.SaveTickets(tickets);

            // SignalR Live Notification
            _hubContext.Clients.All.SendAsync("ReceiveNotification", $"Talebiniz yanıtlandı! Konu: {tickets[ticketIndex].Title}", "User");
            
            return RedirectToAction("Index");
        }

        // 6. Talebi Üstlenme (POST)
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public IActionResult Claim(int id)
        {
            var tickets = JsonDbManager.GetTickets();
            var ticketIndex = tickets.FindIndex(t => t.Id == id);
            
            if (ticketIndex == -1) return NotFound();
            
            tickets[ticketIndex].AssignedAgent = User.Identity?.Name ?? "Destek Elemanı";
            JsonDbManager.SaveTickets(tickets);
            
            return RedirectToAction("Index");
        }

        // 7. Talebi Silme (POST)
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            var tickets = JsonDbManager.GetTickets();
            var ticket = tickets.FirstOrDefault(t => t.Id == id);
            
            if (ticket == null) return NotFound();
            
            tickets.Remove(ticket);
            JsonDbManager.SaveTickets(tickets);
            
            return RedirectToAction("Index");
        }

        // 8. Bilet Detayları (GET)
        [HttpGet]
        public IActionResult Details(int id)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("Login", "Account");
            }

            var tickets = JsonDbManager.GetTickets();
            var ticket = tickets.FirstOrDefault(t => t.Id == id);

            if (ticket == null) return NotFound();

            // Güvenlik kontrolü: Normal kullanıcı sadece kendi biletini görebilir
            var isAdmin = User.IsInRole("Admin");
            if (!isAdmin && ticket.CustomerName != username)
            {
                return Forbid();
            }

            return View(ticket);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public IActionResult Details(int id, string supportReply, TicketStatus status)
        {
            var tickets = JsonDbManager.GetTickets();
            var ticketIndex = tickets.FindIndex(t => t.Id == id);

            if (ticketIndex == -1) return NotFound();

            tickets[ticketIndex].SupportReply = supportReply;
            tickets[ticketIndex].Status = status;

            // Eğer daha önce üstlenilmemişse, otomatik olarak cevaplayan admini ata
            if (string.IsNullOrEmpty(tickets[ticketIndex].AssignedAgent))
            {
                tickets[ticketIndex].AssignedAgent = User.Identity?.Name ?? "Destek Elemanı";
            }

            JsonDbManager.SaveTickets(tickets);

            // SignalR Live Notification
            _hubContext.Clients.All.SendAsync("ReceiveNotification", $"Talebiniz yanıtlandı! Konu: {tickets[ticketIndex].Title}", "User");

            return RedirectToAction("Details", new { id = id });
        }
    }
}