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
                tickets = tickets.Where(t => string.Equals(t.CustomerName, username, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Sayımları filtrelemeden bağımsız olarak, yetki sınırları dahilinde hesaplayıp ViewBag'e atıyoruz
            ViewBag.TotalCount = tickets.Count;
            ViewBag.OpenCount = tickets.Count(t => t.Status == TicketStatus.Acik);
            ViewBag.SolvedCount = tickets.Count(t => t.Status == TicketStatus.Cozuldu);
            ViewBag.ClosedCount = tickets.Count(t => t.Status == TicketStatus.Kapandi);

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

        // 4. Admin Cevaplama Ekranı (GET) - Detay sayfasına yönlendirildi
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult Reply(int id)
        {
            return RedirectToAction("Details", new { id = id });
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
            if (!isAdmin && !string.Equals(ticket.CustomerName, username, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            // Geriye dönük uyumluluk için mesaj listesini doldur
            if (ticket.Messages == null)
            {
                ticket.Messages = new List<TicketMessage>();
            }

            if (!ticket.Messages.Any())
            {
                // Müşterinin ilk mesajı (bilet açıklaması)
                ticket.Messages.Add(new TicketMessage
                {
                    Sender = ticket.CustomerName,
                    Role = "User",
                    Message = ticket.Description,
                    SentDate = ticket.CreatedDate
                });

                // Eğer temsilci cevabı varsa, onu da ekleyelim
                if (!string.IsNullOrEmpty(ticket.SupportReply))
                {
                    ticket.Messages.Add(new TicketMessage
                    {
                        Sender = ticket.AssignedAgent ?? "Destek Elemanı",
                        Role = "Admin",
                        Message = ticket.SupportReply,
                        SentDate = ticket.CreatedDate.AddMinutes(30)
                    });
                }

                // Değişikliği veritabanına kaydet
                JsonDbManager.SaveTickets(tickets);
            }

            return View(ticket);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Details(int id, string message, TicketStatus? status)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("Login", "Account");
            }

            var tickets = JsonDbManager.GetTickets();
            var ticketIndex = tickets.FindIndex(t => t.Id == id);

            if (ticketIndex == -1) return NotFound();

            var ticket = tickets[ticketIndex];
            var isAdmin = User.IsInRole("Admin");

            // Güvenlik Kontrolü: Admin olmayan kullanıcılar sadece kendi biletlerine yazabilir
            if (!isAdmin && !string.Equals(ticket.CustomerName, username, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            // Geriye dönük uyumluluk için mesaj listesini doğrula
            if (ticket.Messages == null)
            {
                ticket.Messages = new List<TicketMessage>();
            }
            if (!ticket.Messages.Any())
            {
                ticket.Messages.Add(new TicketMessage
                {
                    Sender = ticket.CustomerName,
                    Role = "User",
                    Message = ticket.Description,
                    SentDate = ticket.CreatedDate
                });
                if (!string.IsNullOrEmpty(ticket.SupportReply))
                {
                    ticket.Messages.Add(new TicketMessage
                    {
                        Sender = ticket.AssignedAgent ?? "Destek Elemanı",
                        Role = "Admin",
                        Message = ticket.SupportReply,
                        SentDate = ticket.CreatedDate.AddMinutes(30)
                    });
                }
            }

            // Mesaj içeriği boş değilse yeni mesaj ekle
            if (!string.IsNullOrWhiteSpace(message))
            {
                var newMessage = new TicketMessage
                {
                    Sender = username,
                    Role = isAdmin ? "Admin" : "User",
                    Message = message.Trim(),
                    SentDate = DateTime.Now
                };
                ticket.Messages.Add(newMessage);

                // Geriye dönük uyumluluk alanlarını da güncelle
                if (isAdmin)
                {
                    ticket.SupportReply = message.Trim();
                    // Eğer daha önce üstlenilmemişse, otomatik olarak cevaplayan admini ata
                    if (string.IsNullOrEmpty(ticket.AssignedAgent))
                    {
                        ticket.AssignedAgent = username;
                    }
                }
                else
                {
                    // Müşteri yeni bir mesaj yazdığında, eğer bilet kapalıysa veya çözüldüyse "Açık" durumuna geri getirilebilir
                    if (ticket.Status == TicketStatus.Cozuldu || ticket.Status == TicketStatus.Kapandi)
                    {
                        ticket.Status = TicketStatus.Acik;
                    }
                }
            }

            // Durum güncellemesi yapılmışsa uygula
            if (status.HasValue)
            {
                // Müşteri de durum güncelleyebilir (örn: kapatabilir). Admin her şeyi yapabilir.
                if (isAdmin || status.Value == TicketStatus.Kapandi || status.Value == TicketStatus.Cozuldu || status.Value == TicketStatus.Acik)
                {
                    ticket.Status = status.Value;
                }
            }

            JsonDbManager.SaveTickets(tickets);

            // SignalR Canlı Bildirimleri
            if (!string.IsNullOrWhiteSpace(message))
            {
                if (isAdmin)
                {
                    // Müşteriye bildirim gönder
                    _hubContext.Clients.All.SendAsync("ReceiveNotification", $"Talebinize yeni bir yanıt eklendi! Konu: {ticket.Title}", "User");
                }
                else
                {
                    // Admin'e bildirim gönder
                    _hubContext.Clients.All.SendAsync("ReceiveNotification", $"Talebe müşteri tarafından yeni yanıt yazıldı! Konu: {ticket.Title}", "Admin");
                }
            }
            else if (status.HasValue)
            {
                string statusName = status.Value == TicketStatus.Acik ? "Açık" : status.Value == TicketStatus.Cozuldu ? "Çözüldü" : "Kapalı";
                _hubContext.Clients.All.SendAsync("ReceiveNotification", $"Talep durumu güncellendi ({statusName}): {ticket.Title}", isAdmin ? "User" : "Admin");
            }

            return RedirectToAction("Details", new { id = id });
        }
    }
}