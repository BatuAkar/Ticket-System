using System.IO;
using System.Text.Json;
using TicketSistemi.Models;
using TicketSistemi.Utils;

namespace TicketSistemi.Data
{
    public static class JsonDbManager
    {
        private static readonly string filePath = "tickets.json";
        private static readonly string usersFilePath = "users.json";
        private static readonly object _fileLock = new object();

        public static List<Ticket> GetTickets()
        {
            lock (_fileLock)
            {
                if (!File.Exists(filePath)) return new List<Ticket>();
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<List<Ticket>>(json) ?? new List<Ticket>();
            }
        }

        public static void SaveTickets(List<Ticket> tickets)
        {
            lock (_fileLock)
            {
                string json = JsonSerializer.Serialize(tickets, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
        }

        public static List<User> GetUsers()
        {
            lock (_fileLock)
            {
                if (!File.Exists(usersFilePath))
                {
                    var defaultUsers = new List<User>
                    {
                        new User
                        {
                            Id = 1,
                            Username = "admin",
                            PasswordHash = PasswordHelper.HashPassword("admin", "123"),
                            Role = "Admin"
                        },
                        new User
                        {
                            Id = 2,
                            Username = "user",
                            PasswordHash = PasswordHelper.HashPassword("user", "123"),
                            Role = "User"
                        }
                    };
                    string jsonStr = JsonSerializer.Serialize(defaultUsers, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(usersFilePath, jsonStr);
                    return defaultUsers;
                }

                string json = File.ReadAllText(usersFilePath);
                return JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
            }
        }

        public static void SaveUsers(List<User> users)
        {
            lock (_fileLock)
            {
                string json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(usersFilePath, json);
            }
        }
    }
}