using System.ComponentModel.DataAnnotations;

namespace PetitShope.Models;

public class Order
{
    [Key]
    public int Id { get; set; }

    // Optional linking to a registered user
    public int? UserId { get; set; }

    // Stored JSON blob of cart items
    public string ItemsJson { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Shipping information
    public string? ShippingAddress { get; set; }
    public string? ShippingType { get; set; }

    // PayPal transaction details
    public string? PayPalOrderId { get; set; }
    public string? PayPalPayerId { get; set; }
    public string? PayPalPaymentStatus { get; set; } // "PENDING", "COMPLETED", "FAILED"
    public string? PayPalCaptureId { get; set; }
}
