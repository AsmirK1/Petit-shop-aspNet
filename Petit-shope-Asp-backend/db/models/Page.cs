using System.ComponentModel.DataAnnotations;

namespace PetitShope.Models
{
    public class Page
    {
        [Key]
        public string Id { get; set; } = string.Empty; // frontend uses string UUIDs

        public string Title { get; set; } = string.Empty;

        // Foreign key to Business
        public int BusinessId { get; set; }
        public Business? Business { get; set; }

        // Carts (items) on this page
        public ICollection<CartItem> Carts { get; set; } = new List<CartItem>();
    }
}
