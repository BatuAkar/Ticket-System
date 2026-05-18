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
            var tickets = JsonDbManager.GetTickets();
            
            // Genel sayımları filtrelemeden bağımsız olarak hesaplayıp ViewBag'e atıyoruz
            ViewBag.TotalCount = tickets.Count;
            ViewBag.OpenCount = tickets.Count(t => t.Status == TicketStatus.Acik);
            ViewBag.SolvedCount = tickets.Count(t => t.Status == TicketStatus.Cozuldu);

            // LINQ ile filtreleme (Eğer bir durum seçilmişse)
            if (status.HasValue)
            {
                tickets = tickets.Where(t => t.Status == status.Value).ToList();
            }

            // Hoca LINQ istemişti. Tarihe göre ters sıralıyoruz.
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
        public IActionResult Create(Ticket newTicket)
        {
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
            
            JsonDbManager.SaveTickets(tickets);
            
            return RedirectToAction("Index");
        }
    }
}