using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSistemi.Models;
using TicketSistemi.Data;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using TicketSistemi.Hubs;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;
using System;

namespace TicketSistemi.Controllers
{
    [Authorize]
    public class TicketController : Controller
    {
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<TicketController> _logger;
        private readonly IWebHostEnvironment _env;

        public TicketController(IHubContext<NotificationHub> hubContext, ILogger<TicketController> logger, IWebHostEnvironment env)
        {
            _hubContext = hubContext;
            _logger = logger;
            _env = env;
        }

        private async Task<(string? path, string? fileName)> SaveAttachmentAsync(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                return (null, null);
            }

            // 10MB limit (10 * 1024 * 1024 bytes)
            const long maxFileSize = 10485760;
            if (file.Length > maxFileSize)
            {
                throw new ArgumentException("Yüklenen dosya boyutu 10MB'tan büyük olamaz.");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".txt", ".zip", ".rar", ".docx", ".xlsx" };
            if (!allowedExtensions.Contains(extension))
            {
                throw new ArgumentException("İzin verilmeyen dosya formatı. Desteklenen formatlar: PDF, JPG, JPEG, PNG, GIF, WEBP, TXT, ZIP, RAR, DOCX, XLSX");
            }

            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            return ("/uploads/" + uniqueFileName, file.FileName);
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
        public async Task<IActionResult> Create(Ticket newTicket, IFormFile? attachment)
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
                string? attachmentPath = null;
                string? attachmentFileName = null;

                try
                {
                    (attachmentPath, attachmentFileName) = await SaveAttachmentAsync(attachment);
                }
                catch (ArgumentException ex)
                {
                    ModelState.AddModelError("attachment", ex.Message);
                    return View(newTicket);
                }

                newTicket.AttachmentPath = attachmentPath;
                newTicket.AttachmentFileName = attachmentFileName;

                newTicket.Description = TicketSistemi.Utils.HtmlSanitizer.Sanitize(newTicket.Description);

                var tickets = JsonDbManager.GetTickets();
                
                newTicket.Id = tickets.Any() ? tickets.Max(t => t.Id) + 1 : 1;
                
                tickets.Add(newTicket);
                JsonDbManager.SaveTickets(tickets);

                _logger.LogInformation("Yeni destek talebi oluşturuldu. ID: {Id}, Başlık: {Title}, Müşteri: {CustomerName}, Ek: {AttachmentName}", newTicket.Id, newTicket.Title, username, attachmentFileName);

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
            
            var agentName = User.Identity?.Name ?? "Destek Elemanı";
            tickets[ticketIndex].AssignedAgent = agentName;
            JsonDbManager.SaveTickets(tickets);

            _logger.LogInformation("Destek talebi üstlenildi. ID: {Id}, Üstlenen Admin: {AdminName}", id, agentName);
            
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

            // 1. Delete main ticket attachment from disk
            if (!string.IsNullOrEmpty(ticket.AttachmentPath))
            {
                var fullPath = Path.Combine(_env.WebRootPath, ticket.AttachmentPath.TrimStart('/'));
                if (System.IO.File.Exists(fullPath))
                {
                    try { System.IO.File.Delete(fullPath); } catch { /* fail silently */ }
                }
            }

            // 2. Delete all message attachments from disk
            if (ticket.Messages != null)
            {
                foreach (var msg in ticket.Messages)
                {
                    if (!string.IsNullOrEmpty(msg.AttachmentPath))
                    {
                        var msgFullPath = Path.Combine(_env.WebRootPath, msg.AttachmentPath.TrimStart('/'));
                        if (System.IO.File.Exists(msgFullPath))
                        {
                            try { System.IO.File.Delete(msgFullPath); } catch { /* fail silently */ }
                        }
                    }
                }
            }
            
            tickets.Remove(ticket);
            JsonDbManager.SaveTickets(tickets);

            _logger.LogInformation("Destek talebi silindi. ID: {Id}, Başlık: {Title}, Sileyen Admin: {AdminName}", id, ticket.Title, User.Identity?.Name);
            
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
                    SentDate = ticket.CreatedDate,
                    AttachmentPath = ticket.AttachmentPath,
                    AttachmentFileName = ticket.AttachmentFileName
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
        public async Task<IActionResult> Details(int id, string message, TicketStatus? status, IFormFile? attachment, TicketCategory? category, TicketPriority? priority)
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
                    SentDate = ticket.CreatedDate,
                    AttachmentPath = ticket.AttachmentPath,
                    AttachmentFileName = ticket.AttachmentFileName
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

            // Kategori ve Öncelik güncellemeleri (Yalnızca Admin)
            var oldCategory = ticket.Category;
            var oldPriority = ticket.Priority;
            if (isAdmin)
            {
                if (category.HasValue)
                {
                    ticket.Category = category.Value;
                }
                if (priority.HasValue)
                {
                    ticket.Priority = priority.Value;
                }
                // Eğer daha önce üstlenilmemişse, otomatik olarak işlem yapan admini ata
                if (string.IsNullOrEmpty(ticket.AssignedAgent))
                {
                    ticket.AssignedAgent = username;
                }
            }

            var oldStatus = ticket.Status;

            // Mesaj içeriği veya attachment varsa yeni mesaj ekle
            if (!string.IsNullOrWhiteSpace(message) || (attachment != null && attachment.Length > 0))
            {
                string? attachmentPath = null;
                string? attachmentFileName = null;

                try
                {
                    (attachmentPath, attachmentFileName) = await SaveAttachmentAsync(attachment);
                }
                catch (ArgumentException ex)
                {
                    TempData["ErrorMessage"] = ex.Message;
                    return RedirectToAction("Details", new { id = id });
                }

                var newMessage = new TicketMessage
                {
                    Sender = username,
                    Role = isAdmin ? "Admin" : "User",
                    Message = !string.IsNullOrWhiteSpace(message) ? TicketSistemi.Utils.HtmlSanitizer.Sanitize(message.Trim()) : "",
                    SentDate = DateTime.Now,
                    AttachmentPath = attachmentPath,
                    AttachmentFileName = attachmentFileName
                };
                ticket.Messages.Add(newMessage);

                // Geriye dönük uyumluluk alanlarını da güncelle
                if (isAdmin)
                {
                    ticket.SupportReply = newMessage.Message;
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

                _logger.LogInformation("Destek talebine yanıt yazıldı. ID: {Id}, Yazan: {Username}, Rol: {Role}, Ek: {AttachmentName}", id, username, isAdmin ? "Admin" : "User", attachmentFileName);
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

            // Log changes as system messages in the conversation history
            if (isAdmin && oldCategory != ticket.Category)
            {
                ticket.Messages.Add(new TicketMessage
                {
                    Sender = "Sistem",
                    Role = "Admin",
                    Message = $"Kategori '{TicketSistemi.Models.EnumHelper.GetCategoryName(oldCategory)}' değerinden '{TicketSistemi.Models.EnumHelper.GetCategoryName(ticket.Category)}' değerine güncellendi.",
                    SentDate = DateTime.Now
                });
            }

            if (isAdmin && oldPriority != ticket.Priority)
            {
                ticket.Messages.Add(new TicketMessage
                {
                    Sender = "Sistem",
                    Role = "Admin",
                    Message = $"Öncelik seviyesi '{TicketSistemi.Models.EnumHelper.GetPriorityName(oldPriority)}' değerinden '{TicketSistemi.Models.EnumHelper.GetPriorityName(ticket.Priority)}' değerine güncellendi.",
                    SentDate = DateTime.Now
                });
            }

            if (oldStatus != ticket.Status)
            {
                string oldStatusName = oldStatus == TicketStatus.Acik ? "Açık" : oldStatus == TicketStatus.Cozuldu ? "Çözüldü" : "Kapalı";
                string newStatusName = ticket.Status == TicketStatus.Acik ? "Açık" : ticket.Status == TicketStatus.Cozuldu ? "Çözüldü" : "Kapalı";
                ticket.Messages.Add(new TicketMessage
                {
                    Sender = "Sistem",
                    Role = "Admin",
                    Message = $"Bilet durumu '{oldStatusName}' değerinden '{newStatusName}' değerine güncellendi.",
                    SentDate = DateTime.Now
                });

                _logger.LogInformation("Destek talebi durumu güncellendi. ID: {Id}, Eski Durum: {OldStatus}, Yeni Durum: {NewStatus}, Güncelleyen: {Username}", id, oldStatus, ticket.Status, username);
            }

            JsonDbManager.SaveTickets(tickets);

            // SignalR Canlı Bildirimleri
            if (!string.IsNullOrWhiteSpace(message) || (attachment != null && attachment.Length > 0))
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
            else if (status.HasValue && oldStatus != ticket.Status)
            {
                string statusName = status.Value == TicketStatus.Acik ? "Açık" : status.Value == TicketStatus.Cozuldu ? "Çözüldü" : "Kapalı";
                _hubContext.Clients.All.SendAsync("ReceiveNotification", $"Talep durumu güncellendi ({statusName}): {ticket.Title}", isAdmin ? "User" : "Admin");
            }

            return RedirectToAction("Details", new { id = id });
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult Dashboard()
        {
            var tickets = JsonDbManager.GetTickets();

            int totalTickets = tickets.Count;
            int openCount = tickets.Count(t => t.Status == TicketStatus.Acik);
            int solvedCount = tickets.Count(t => t.Status == TicketStatus.Cozuldu);
            int closedCount = tickets.Count(t => t.Status == TicketStatus.Kapandi);

            var categoryStats = tickets.GroupBy(t => t.Category)
                                       .ToDictionary(g => EnumHelper.GetCategoryName(g.Key), g => g.Count());
            
            foreach (TicketCategory cat in Enum.GetValues(typeof(TicketCategory)))
            {
                var catName = EnumHelper.GetCategoryName(cat);
                if (!categoryStats.ContainsKey(catName))
                {
                    categoryStats[catName] = 0;
                }
            }

            var priorityStats = tickets.GroupBy(t => t.Priority)
                                       .ToDictionary(g => EnumHelper.GetPriorityName(g.Key), g => g.Count());
            
            foreach (TicketPriority pri in Enum.GetValues(typeof(TicketPriority)))
            {
                var priName = EnumHelper.GetPriorityName(pri);
                if (!priorityStats.ContainsKey(priName))
                {
                    priorityStats[priName] = 0;
                }
            }

            var trendStats = new Dictionary<string, int>();
            var today = DateTime.Today;
            for (int i = 6; i >= 0; i--)
            {
                var date = today.AddDays(-i);
                var dateStr = date.ToString("dd.MM.yyyy");
                trendStats[dateStr] = tickets.Count(t => t.CreatedDate.Date == date);
            }

            double avgReplies = 0;
            if (totalTickets > 0)
            {
                avgReplies = tickets.Average(t => t.Messages != null ? Math.Max(0, t.Messages.Count - 1) : 0);
            }

            ViewBag.TotalTickets = totalTickets;
            ViewBag.OpenCount = openCount;
            ViewBag.SolvedCount = solvedCount;
            ViewBag.ClosedCount = closedCount;
            ViewBag.CategoryStats = categoryStats;
            ViewBag.PriorityStats = priorityStats;
            ViewBag.TrendStats = trendStats;
            ViewBag.AvgReplies = Math.Round(avgReplies, 1);

            return View();
        }
    }
}