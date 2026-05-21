using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TicketSistemi.Data;
using TicketSistemi.Models;

namespace TicketSistemi.Jobs
{
    public class AutoCloseTicketsJob : BackgroundService
    {
        private readonly ILogger<AutoCloseTicketsJob> _logger;
        // Run every 12 hours (twice a day)
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(12);

        public AutoCloseTicketsJob(ILogger<AutoCloseTicketsJob> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Biletleri otomatik kapatma arka plan servisi başlatıldı.");

            // Wait a few seconds initially to let the application start up fully
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Otomatik bilet kapatma kontrolü çalıştırılıyor...");
                    AutoCloseSolvedTickets();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Otomatik bilet kapatma işlemi sırasında bir hata oluştu.");
                }

                try
                {
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("Biletleri otomatik kapatma arka plan servisi durduruldu.");
        }

        private void AutoCloseSolvedTickets()
        {
            var tickets = JsonDbManager.GetTickets();
            bool isAnyUpdated = false;
            var now = DateTime.Now;

            // Cozuldu (Solved) status tickets
            var solvedTickets = tickets.Where(t => t.Status == TicketStatus.Cozuldu).ToList();

            foreach (var ticket in solvedTickets)
            {
                // Inactivity check: 3 days (3 * 24 hours)
                var lastActivity = ticket.Messages != null && ticket.Messages.Any()
                    ? ticket.Messages.Max(m => m.SentDate)
                    : ticket.CreatedDate;

                if (now - lastActivity >= TimeSpan.FromDays(3))
                {
                    ticket.Status = TicketStatus.Kapandi;
                    
                    // Add system message if messages list is available
                    if (ticket.Messages == null)
                    {
                        ticket.Messages = new System.Collections.Generic.List<TicketMessage>();
                    }

                    ticket.Messages.Add(new TicketMessage
                    {
                        Sender = "Sistem",
                        Role = "Admin",
                        Message = "Çözüldü olarak işaretlenen bu bilet, 3 gün boyunca işlem yapılmadığı için sistem tarafından otomatik olarak kapatılmıştır.",
                        SentDate = now
                    });

                    _logger.LogInformation("Bilet otomatik olarak kapatıldı. ID: {Id}, Başlık: {Title}, Son Aktivite: {LastActivity}", ticket.Id, ticket.Title, lastActivity);
                    isAnyUpdated = true;
                }
            }

            if (isAnyUpdated)
            {
                JsonDbManager.SaveTickets(tickets);
                _logger.LogInformation("Otomatik kapatılan biletler kaydedildi.");
            }
            else
            {
                _logger.LogInformation("Otomatik kapatılması gereken herhangi bir bilet bulunamadı.");
            }
        }
    }
}
