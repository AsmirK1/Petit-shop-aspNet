using System.ComponentModel.DataAnnotations;

namespace PetitShope.Models
{
    public class CartItem
    {
        [Key]
        public string Id { get; set; } = string.Empty; // frontend uses string UUIDs

        public string Title { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string Category { get; set; } = string.Empty;

        public string? Image { get; set; }

        // Foreign key to Page
        public string PageId { get; set; } = string.Empty;
        public Page? Page { get; set; }
    }
}
