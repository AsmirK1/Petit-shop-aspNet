
using System.ComponentModel.DataAnnotations;

namespace PetitShope.Models
{
    public class Business
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        // Owner user id (seller)
        public int? OwnerId { get; set; }
        public User? Owner { get; set; }
        // Pages created by the seller (matches frontend `pages` structure)
        public ICollection<Page> Pages { get; set; } = new List<Page>();
    }
}
