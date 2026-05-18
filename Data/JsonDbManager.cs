using System.Text.Json;
using TicketSistemi.Models;

namespace TicketSistemi.Data
{
    public static class JsonDbManager
    {
        private static readonly string filePath = "tickets.json";

        public static List<Ticket> GetTickets()
        {
            if (!File.Exists(filePath)) return new List<Ticket>();
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<Ticket>>(json) ?? new List<Ticket>();
        }

        public static void SaveTickets(List<Ticket> tickets)
        {
            string json = JsonSerializer.Serialize(tickets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
    }
}