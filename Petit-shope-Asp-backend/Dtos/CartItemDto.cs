namespace PetitShope.Dtos;

public class CartItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string PageId { get; set; } = string.Empty;
}
