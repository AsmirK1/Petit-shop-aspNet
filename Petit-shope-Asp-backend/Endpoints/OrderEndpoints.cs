using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PetitShope.Models;
using System.Text.Json;
using System.Text;

namespace PetitShope.Endpoints;

public static class OrderEndpoints
{
    // Helper method to send order confirmation email to buyer
    private static async Task<bool> SendOrderConfirmationToBuyer(
        string buyerEmail,
        string buyerName,
        int orderId,
        List<CartItemInfo> items,
        decimal total,
        ILogger logger,
        IConfiguration cfg
    )
    {
        try
        {
            using var httpClient = new HttpClient();
            var expressUrl = cfg["Express:Url"] ?? "http://localhost:4000";
            var endpoint = expressUrl.TrimEnd('/') + "/api/email/send-order-confirmation-buyer";

            var payload = new
            {
                email = buyerEmail,
                name = buyerName,
                orderId = orderId,
                items = items,
                total = total
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await httpClient.PostAsync(endpoint, content);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("✅ Order confirmation email sent to buyer {Email}", buyerEmail);
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("❌ Failed to send order confirmation to buyer: {Error}", errorContent);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error sending order confirmation to buyer");
            return false;
        }
    }

    // Helper method to send order notification email to seller
    private static async Task<bool> SendOrderNotificationToSeller(
        string sellerEmail,
        string sellerName,
        int orderId,
        List<CartItemInfo> sellerItems,
        decimal totalForSeller,
        string buyerName,
        ILogger logger,
        IConfiguration cfg
    )
    {
        try
        {
            using var httpClient = new HttpClient();
            var expressUrl = cfg["Express:Url"] ?? "http://localhost:4000";
            var endpoint = expressUrl.TrimEnd('/') + "/api/email/send-order-notification-seller";

            var payload = new
            {
                email = sellerEmail,
                sellerName = sellerName,
                orderId = orderId,
                items = sellerItems,
                totalForSeller = totalForSeller,
                buyerName = buyerName
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await httpClient.PostAsync(endpoint, content);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("✅ Order notification sent to seller {Email}", sellerEmail);
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("❌ Failed to send order notification to seller: {Error}", errorContent);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error sending order notification to seller");
            return false;
        }
    }
    public static void MapOrderEndpoints(this WebApplication app)
    {
        app.MapPost("/api/orders", async ([FromBody] OrderCreateDto dto, AppDbContext db, ILogger<Program> logger, ClaimsPrincipal user) =>
        {
            try
            {
                // If caller is authenticated, use their id as the order's UserId (prevent spoofing)
                int? userId = dto.UserId;
                if (user?.Identity?.IsAuthenticated == true)
                {
                    var idClaim = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    if (int.TryParse(idClaim, out var uid)) userId = uid;
                }

                var order = new Order
                {
                    UserId = userId,
                    ItemsJson = dto.ItemsJson ?? string.Empty,
                    Total = dto.Total
                    ,
                    ShippingAddress = JsonSerializer.Serialize(new
                    {
                        fullName = dto.ShippingFullName,
                        address1 = dto.ShippingAddress1,
                        address2 = dto.ShippingAddress2,
                        city = dto.ShippingCity,
                        state = dto.ShippingState,
                        postalCode = dto.ShippingPostalCode,
                        country = dto.ShippingCountry,
                        phone = dto.ShippingPhone
                    }),
                    ShippingType = dto.ShippingType
                };
                db.Add(order);
                await db.SaveChangesAsync();
                return Results.Created($"/api/orders/{order.Id}", order);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save order");
                return Results.Problem(ex.Message);
            }
        });

        app.MapGet("/api/orders/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var o = await db.Orders.FindAsync(id);
            if (o is null) return Results.NotFound();

            // Require authentication to view an order. Sellers can view any order; buyers can view only their orders.
            if (user?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
            var role = user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var idClaim = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int? uid = null;
            if (int.TryParse(idClaim, out var parsed)) uid = parsed;

            if (role != "Seller" && o.UserId != uid) return Results.Forbid();

            return Results.Ok(o);
        }).RequireAuthorization();

        // PayPal: Create order (get sellers' merchant IDs from cart items)
        app.MapPost("/api/orders/paypal/create", async (
            [FromBody] PayPalCreateOrderDto dto,
            AppDbContext db,
            ILogger<Program> logger,
            ClaimsPrincipal user
        ) =>
        {
            try
            {
                // Get authenticated user ID if available
                int? userId = null;
                if (user?.Identity?.IsAuthenticated == true)
                {
                    var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (int.TryParse(idClaim, out var uid))
                    {
                        userId = uid;
                        logger.LogInformation("Authenticated user ID: {UserId}", userId);
                    }
                }
                else
                {
                    logger.LogWarning("User is not authenticated for PayPal order creation");
                }

                // Parse cart items to get seller info
                var cartItems = JsonSerializer.Deserialize<List<CartItemInfo>>(dto.ItemsJson ?? "[]");
                if (cartItems == null || cartItems.Count == 0)
                {
                    return Results.BadRequest(new { error = "Cart is empty" });
                }

                logger.LogInformation("Creating PayPal order with {Count} items", cartItems.Count);

                // Get unique seller IDs from cart items
                var sellerIds = cartItems.Select(item => item.SellerId).Distinct().ToList();

                // Fetch sellers and their PayPal Merchant IDs from Businesses
                var businesses = await db.Businesses
                    .Where(b => sellerIds.Contains(b.Id))
                    .Include(b => b.Owner)
                    .ToListAsync();

                logger.LogInformation("Found {Count} businesses for sellers", businesses.Count);

                // Check if all sellers have PayPal configured
                var sellersWithoutPayPal = businesses.Where(b => string.IsNullOrWhiteSpace(b.Owner?.PayPalMerchantId)).ToList();
                if (sellersWithoutPayPal.Any())
                {
                    var sellerNames = string.Join(", ", sellersWithoutPayPal.Select(b => b.Name));
                    return Results.BadRequest(new
                    {
                        error = $"⚠️ The following sellers have not configured PayPal: {sellerNames}. Please ask them to add their PayPal Merchant ID in their profile."
                    });
                }

                // Return seller merchant IDs to frontend
                var merchantIds = businesses.Select(b => b.Owner?.PayPalMerchantId).Where(m => m != null).ToList();

                logger.LogInformation("Sellers: {SellerCount}, With PayPal: {PayPalCount}",
                    businesses.Count, merchantIds.Count);

                // Create pending order in database
                var order = new Order
                {
                    UserId = userId,
                    ItemsJson = dto.ItemsJson ?? string.Empty,
                    Total = dto.Total,
                    PayPalPaymentStatus = "PENDING"
                    ,
                    ShippingAddress = JsonSerializer.Serialize(new
                    {
                        fullName = dto.ShippingFullName,
                        address1 = dto.ShippingAddress1,
                        address2 = dto.ShippingAddress2,
                        city = dto.ShippingCity,
                        state = dto.ShippingState,
                        postalCode = dto.ShippingPostalCode,
                        country = dto.ShippingCountry,
                        phone = dto.ShippingPhone
                    }),
                    ShippingType = dto.ShippingType
                };
                db.Add(order);
                await db.SaveChangesAsync();

                logger.LogInformation("Created PayPal order {OrderId} for user {UserId}, total: {Total}",
                    order.Id, userId, dto.Total);

                return Results.Ok(new
                {
                    orderId = order.Id,
                    merchantIds = merchantIds,
                    total = dto.Total,
                    currency = "USD"
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create PayPal order");
                return Results.Problem(ex.Message);
            }
        });

        // PayPal: Capture payment after approval
        app.MapPost("/api/orders/paypal/capture", async (
            [FromBody] PayPalCaptureDto dto,
            AppDbContext db,
            ILogger<Program> logger,
            IConfiguration cfg
        ) =>
        {
            try
            {
                var order = await db.Orders.FindAsync(dto.OrderId);
                if (order == null)
                {
                    return Results.NotFound(new { error = "Order not found" });
                }

                // Update order with PayPal transaction details
                order.PayPalOrderId = dto.PayPalOrderId;
                order.PayPalPayerId = dto.PayPalPayerId;
                order.PayPalPaymentStatus = dto.Status;
                order.PayPalCaptureId = dto.CaptureId;

                await db.SaveChangesAsync();

                logger.LogInformation("Captured PayPal payment for order {OrderId}, status: {Status}",
                    order.Id, dto.Status);

                // Send confirmation emails if payment was successful
                if (dto.Status == "COMPLETED" || dto.Status == "APPROVED")
                {
                    try
                    {
                        // Parse order items
                        var items = JsonSerializer.Deserialize<List<CartItemInfo>>(order.ItemsJson ?? "[]");
                        if (items != null && items.Count > 0)
                        {
                            // Get buyer information
                            User? buyer = null;
                            if (order.UserId.HasValue)
                            {
                                buyer = await db.Users.FindAsync(order.UserId.Value);
                            }

                            // Send confirmation to buyer
                            if (buyer != null)
                            {
                                await SendOrderConfirmationToBuyer(
                                    buyer.Email,
                                    buyer.Name,
                                    order.Id,
                                    items,
                                    order.Total,
                                    logger,
                                    cfg
                                );
                            }
                            else
                            {
                                logger.LogWarning("Cannot send buyer confirmation: buyer not found for order {OrderId}", order.Id);
                            }

                            // Group items by seller and send notifications
                            var itemsBySeller = items.GroupBy(item => item.SellerId);

                            foreach (var sellerGroup in itemsBySeller)
                            {
                                var sellerId = sellerGroup.Key;
                                var sellerItems = sellerGroup.ToList();

                                // Get seller's business and email
                                var business = await db.Businesses
                                    .Include(b => b.Owner)
                                    .FirstOrDefaultAsync(b => b.Id == sellerId);

                                if (business?.Owner != null)
                                {
                                    // Calculate total for this seller
                                    var sellerTotal = sellerItems.Sum(item => item.Price * item.Quantity);

                                    await SendOrderNotificationToSeller(
                                        business.Owner.Email,
                                        business.Owner.Name,
                                        order.Id,
                                        sellerItems,
                                        sellerTotal,
                                        buyer?.Name ?? "Customer",
                                        logger,
                                        cfg
                                    );
                                }
                                else
                                {
                                    logger.LogWarning("Cannot send seller notification: seller {SellerId} not found", sellerId);
                                }
                            }
                        }
                    }
                    catch (Exception emailEx)
                    {
                        // Don't fail the order if email sending fails
                        logger.LogError(emailEx, "Failed to send order confirmation emails for order {OrderId}", order.Id);
                    }
                }

                return Results.Ok(new
                {
                    message = "Payment captured successfully",
                    order = new
                    {
                        order.Id,
                        order.Total,
                        order.PayPalPaymentStatus,
                        order.CreatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to capture PayPal payment");
                return Results.Problem(ex.Message);
            }
        });
    }
}

// DTOs
public class CartItemInfo
{
    public int SellerId { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

public class PayPalCreateOrderDto
{
    public string? ItemsJson { get; set; }
    public decimal Total { get; set; }
    public string? ShippingType { get; set; }
    public string? ShippingFullName { get; set; }
    public string? ShippingAddress1 { get; set; }
    public string? ShippingAddress2 { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingPostalCode { get; set; }
    public string? ShippingCountry { get; set; }
    public string? ShippingPhone { get; set; }
}

public class PayPalCaptureDto
{
    public int OrderId { get; set; }
    public string PayPalOrderId { get; set; } = "";
    public string PayPalPayerId { get; set; } = "";
    public string Status { get; set; } = "";
    public string CaptureId { get; set; } = "";
}

public class OrderCreateDto
{
    public int? UserId { get; set; }
    public string? ItemsJson { get; set; }
    public decimal Total { get; set; }
    public string? ShippingType { get; set; }
    public string? ShippingFullName { get; set; }
    public string? ShippingAddress1 { get; set; }
    public string? ShippingAddress2 { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingPostalCode { get; set; }
    public string? ShippingCountry { get; set; }
    public string? ShippingPhone { get; set; }
}
