using System.ComponentModel.DataAnnotations;

namespace TicketSistemi.Models
{
    public class Ticket
    {
        public int Id { get; set; }
        
        [Required(ErrorMessage = "Lütfen bir başlık giriniz.")]
        [StringLength(100, ErrorMessage = "Başlık 100 karakterden uzun olamaz.")]
        public string Title { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Sorununuzu detaylıca açıklamanız gerekmektedir.")]
        public string Description { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Adınızı girmeniz zorunludur.")]
        public string CustomerName { get; set; } = string.Empty; 
        
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public TicketStatus Status { get; set; } = TicketStatus.Acik;
        public string? SupportReply { get; set; } 
        public string? AssignedAgent { get; set; }

        // Mesaj geçmişi
        public List<TicketMessage> Messages { get; set; } = new List<TicketMessage>();
    }

    public class TicketMessage
    {
        public string Sender { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty; // "Admin" or "User"
        public string Message { get; set; } = string.Empty;
        public DateTime SentDate { get; set; } = DateTime.Now;
    }
}