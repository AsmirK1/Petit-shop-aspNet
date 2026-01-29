using System.Collections.Generic;

namespace PetitShope.Dtos;

public class BusinessDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public int? OwnerId { get; set; }
    public List<PageDto> Pages { get; set; } = new List<PageDto>();
}
