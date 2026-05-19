using System.ComponentModel.DataAnnotations;

namespace TicketSistemi.Models
{
    public class Ticket
    {
        public int Id { get; set; }
        
        [Required(ErrorMessage = "Lütfen bir başlık giriniz.")]
        [StringLength(100, ErrorMessage = "Başlık 100 karakterden uzun olamaz.")]
        public string Title { get; set; }
        
        [Required(ErrorMessage = "Sorununuzu detaylıca açıklamanız gerekmektedir.")]
        public string Description { get; set; }
        
        [Required(ErrorMessage = "Adınızı girmeniz zorunludur.")]
        public string CustomerName { get; set; } 
        
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public TicketStatus Status { get; set; } = TicketStatus.Acik;
        public string? SupportReply { get; set; } 
        public string? AssignedAgent { get; set; }
    }
}