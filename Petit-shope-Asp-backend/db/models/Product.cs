namespace PetitShope.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? Image { get; set; }
        public int BusinessId { get; set; }
        public Business? Business { get; set; }
    }
}
