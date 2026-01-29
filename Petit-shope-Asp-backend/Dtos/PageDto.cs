using System.Collections.Generic;

namespace PetitShope.Dtos;

public class PageDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int BusinessId { get; set; }
    public List<CartItemDto> Carts { get; set; } = new List<CartItemDto>();
}
