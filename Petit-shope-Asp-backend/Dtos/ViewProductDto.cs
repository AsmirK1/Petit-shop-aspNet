namespace PetitShope.Dtos;

public class ViewProductDto
{
    // keep id as string to support both DB Product (int) and CartItem (string)
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Image { get; set; }
    public int? BusinessId { get; set; }
}
