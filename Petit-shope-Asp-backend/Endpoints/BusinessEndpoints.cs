using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PetitShope.Models;
using PetitShope.Dtos;

namespace PetitShope.Endpoints;

public static class BusinessEndpoints
{
    public static void MapBusinessEndpoints(this WebApplication app)
    {
        app.MapGet("/api/businesses", async (AppDbContext db, int? ownerId) =>
        {
            var query = db.Businesses.AsQueryable();
            if (ownerId.HasValue) query = query.Where(b => b.OwnerId == ownerId.Value);
            return await query
                .Select(b => new BusinessDto
                {
                    Id = b.Id,
                    Name = b.Name,
                    Category = b.Category,
                    Country = b.Country,
                    City = b.City,
                    OwnerId = b.OwnerId,
                    Pages = b.Pages.Select(p => new PageDto
                    {
                        Id = p.Id,
                        Title = p.Title,
                        BusinessId = p.BusinessId,
                        Carts = p.Carts.Select(c => new CartItemDto
                        {
                            Id = c.Id,
                            Title = c.Title,
                            Price = c.Price,
                            Category = c.Category,
                            Image = c.Image,
                            PageId = c.PageId
                        }).ToList()
                    }).ToList()
                })
                .ToListAsync();
        });

        app.MapPost("/api/businesses", async ([FromBody] Business business, AppDbContext db, ILogger<Program> logger, ClaimsPrincipal user) =>
        {
            try
            {
                // Require that only authenticated sellers create businesses and set OwnerId from token
                var idClaim = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!user?.Identity?.IsAuthenticated ?? true) return Results.Unauthorized();
                if (!int.TryParse(idClaim, out var uid)) return Results.Unauthorized();
                business.OwnerId = uid;
                db.Businesses.Add(business);
                var changed = await db.SaveChangesAsync();
                logger.LogInformation("Saved business {Name} with {Changes} changes", business.Name, changed);
                var saved = await db.Businesses
                    .Where(b => b.Id == business.Id)
                    .Select(b => new BusinessDto
                    {
                        Id = b.Id,
                        Name = b.Name,
                        Category = b.Category,
                        Country = b.Country,
                        City = b.City,
                        OwnerId = b.OwnerId,
                        Pages = b.Pages.Select(p => new PageDto
                        {
                            Id = p.Id,
                            Title = p.Title,
                            BusinessId = p.BusinessId,
                            Carts = p.Carts.Select(c => new CartItemDto
                            {
                                Id = c.Id,
                                Title = c.Title,
                                Price = c.Price,
                                Category = c.Category,
                                Image = c.Image,
                                PageId = c.PageId
                            }).ToList()
                        }).ToList()
                    })
                    .FirstOrDefaultAsync();
                return Results.Created($"/api/businesses/{business.Id}", saved ?? new BusinessDto { Id = business.Id, Name = business.Name, Category = business.Category, Country = business.Country, City = business.City });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save business");
                return Results.Problem(ex.Message);
            }
        }).RequireAuthorization("SellerOnly");

        // only sellers may update or delete businesses; enforce owner match
        app.MapPut("/api/businesses/{id}", async (int id, [FromBody] Business updated, AppDbContext db, ClaimsPrincipal user) =>
        {
            var existing = await db.Businesses.FindAsync(id);
            if (existing == null) return Results.NotFound();
            var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idClaim, out var uid)) return Results.Unauthorized();
            if (existing.OwnerId != uid) return Results.Json(new { error = "You are not the owner of this business" }, statusCode: 403);
            existing.Name = updated.Name;
            existing.Category = updated.Category;
            existing.Country = updated.Country;
            existing.City = updated.City;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("SellerOnly");

        app.MapDelete("/api/businesses/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var existing = await db.Businesses.FindAsync(id);
            if (existing == null) return Results.NotFound();
            var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idClaim, out var uid)) return Results.Unauthorized();
            if (existing.OwnerId != uid) return Results.Json(new { error = "You are not the owner of this business" }, statusCode: 403);
            db.Businesses.Remove(existing);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("SellerOnly");

        // (duplicate non-authorized PUT/DELETE removed)

        app.MapGet("/api/businesses/{id}", async (int id, AppDbContext db) =>
        {
            var b = await db.Businesses
                .Where(x => x.Id == id)
                .Select(b => new BusinessDto
                {
                    Id = b.Id,
                    Name = b.Name,
                    Category = b.Category,
                    Country = b.Country,
                    City = b.City,
                    OwnerId = b.OwnerId,
                    Pages = b.Pages.Select(p => new PageDto
                    {
                        Id = p.Id,
                        Title = p.Title,
                        BusinessId = p.BusinessId,
                        Carts = p.Carts.Select(c => new CartItemDto
                        {
                            Id = c.Id,
                            Title = c.Title,
                            Price = c.Price,
                            Category = c.Category,
                            Image = c.Image,
                            PageId = c.PageId
                        }).ToList()
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            return b is null ? Results.NotFound() : Results.Ok(b);
        });

        // Seller-only endpoint: get businesses owned by the authenticated seller
        app.MapGet("/api/management/businesses", async (AppDbContext db, ClaimsPrincipal user, HttpRequest req, ILogger<Program> logger) =>
        {
            var authHeader = req.Headers["Authorization"].FirstOrDefault();
            logger.LogInformation("GET /api/management/businesses Authorization: {AuthHeader}", authHeader ?? "(none)");
            if (user?.Identity?.IsAuthenticated != true)
            {
                logger.LogWarning("Unauthorized access attempt to management businesses");
                return Results.Unauthorized();
            }

            var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idClaim, out var uid))
            {
                logger.LogWarning("Invalid NameIdentifier claim when accessing management businesses: {Claim}", idClaim);
                return Results.Unauthorized();
            }

            // Verify the user exists and has Seller role in the DB (extra safety)
            var userExists = await db.Users.AnyAsync(u => u.Id == uid);
            if (!userExists)
            {
                logger.LogWarning("Authenticated user {Uid} not found in database", uid);
                return Results.Unauthorized();
            }

            var list = await db.Businesses
                .Where(b => b.OwnerId == uid)
                .Select(b => new BusinessDto
                {
                    Id = b.Id,
                    Name = b.Name,
                    Category = b.Category,
                    Country = b.Country,
                    City = b.City,
                    OwnerId = b.OwnerId,
                    Pages = b.Pages.Select(p => new PageDto
                    {
                        Id = p.Id,
                        Title = p.Title,
                        BusinessId = p.BusinessId,
                        Carts = p.Carts.Select(c => new CartItemDto
                        {
                            Id = c.Id,
                            Title = c.Title,
                            Price = c.Price,
                            Category = c.Category,
                            Image = c.Image,
                            PageId = c.PageId
                        }).ToList()
                    }).ToList()
                })
                .ToListAsync();

            logger.LogInformation("Seller {Uid} requested management businesses, returning {Count} items: {Ids}", uid, list.Count, string.Join(',', list.Select(x => x.Id)));

            return Results.Ok(list);
        }).RequireAuthorization("SellerOnly");

        // ADMIN: delete all businesses (dangerous) - protected by config or only enabled in Development
        app.MapPost("/api/admin/delete-all-businesses", async (AppDbContext db, IConfiguration cfg, IWebHostEnvironment env, HttpRequest req, ILogger<Program> logger) =>
        {
            // Allow only in Development or when Admin:AllowDeleteAll=true and matching secret provided
            var allowInDev = env.IsDevelopment();
            var allowFlag = string.Equals(cfg["Admin:AllowDeleteAll"], "true", StringComparison.OrdinalIgnoreCase);
            var secretConfigured = !string.IsNullOrEmpty(cfg["Admin:Secret"]);
            var provided = req.Headers["X-Admin-Secret"].FirstOrDefault();

            if (!(allowInDev || allowFlag))
            {
                logger.LogWarning("Attempt to call delete-all-businesses blocked (not allowed in this environment)");
                return Results.Forbid();
            }

            if (secretConfigured && string.IsNullOrEmpty(provided))
            {
                logger.LogWarning("Attempt to call delete-all-businesses without secret header");
                return Results.Unauthorized();
            }

            if (secretConfigured && provided != cfg["Admin:Secret"])
            {
                logger.LogWarning("Attempt to call delete-all-businesses with invalid secret");
                return Results.Unauthorized();
            }

            try
            {
                var all = await db.Businesses.ToListAsync();
                var count = all.Count;
                if (count == 0) return Results.Ok(new { deleted = 0 });
                db.Businesses.RemoveRange(all);
                await db.SaveChangesAsync();
                logger.LogInformation("Admin deleted all businesses: {Count}", count);
                return Results.Ok(new { deleted = count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete all businesses");
                return Results.Problem(ex.InnerException?.Message ?? ex.Message);
            }
        });

        // Get seller's MerchantId from cart items (product IDs)
        app.MapPost("/api/businesses/merchant-id", async ([FromBody] List<string> productIds, AppDbContext db, ILogger<Program> logger) =>
        {
            logger.LogInformation("Received merchant-id request with {Count} product IDs: {Ids}", productIds?.Count ?? 0, string.Join(", ", productIds ?? new List<string>()));

            if (productIds == null || productIds.Count == 0)
                return Results.BadRequest(new { error = "Product IDs are required" });

            // Try to parse as int IDs for Products table
            var intIds = productIds.Where(id => int.TryParse(id, out _)).Select(int.Parse).ToList();
            logger.LogInformation("Parsed {Count} int IDs from product IDs", intIds.Count);

            if (intIds.Count > 0)
            {
                // Find first product to get the business owner
                var product = await db.Products
                    .Where(p => intIds.Contains(p.Id))
                    .Include(p => p.Business)
                        .ThenInclude(b => b!.Owner)
                    .FirstOrDefaultAsync();

                logger.LogInformation("Product lookup result: {Found}, BusinessId: {BizId}, OwnerId: {OwnerId}, PayPalMerchantId: {MerchantId}",
                    product != null, product?.BusinessId, product?.Business?.OwnerId, product?.Business?.Owner?.PayPalMerchantId);

                if (product?.Business?.Owner != null)
                {
                    if (string.IsNullOrEmpty(product.Business.Owner.PayPalMerchantId))
                        return Results.NotFound(new { error = "Seller has not configured PayPal Merchant ID" });

                    return Results.Ok(new { merchantId = product.Business.Owner.PayPalMerchantId });
                }
            }

            // Try CartItems with string IDs - need to query through Page->Business->Owner
            logger.LogInformation("Trying CartItems lookup with string IDs");

            // First, get the CartItem to find its Page
            var cartItemWithPage = await db.CartItems
                .Where(c => productIds.Contains(c.Id))
                .Select(c => new { c.Id, c.PageId })
                .FirstOrDefaultAsync();

            if (cartItemWithPage != null)
            {
                logger.LogInformation("Found CartItem {CartId} with PageId {PageId}", cartItemWithPage.Id, cartItemWithPage.PageId);

                // Now get the Business through Page
                var page = await db.Pages
                    .Where(p => p.Id == cartItemWithPage.PageId)
                    .Include(p => p.Business)
                        .ThenInclude(b => b!.Owner)
                    .FirstOrDefaultAsync();

                logger.LogInformation("Page lookup: Found={Found}, BusinessId={BizId}, OwnerId={OwnerId}, PayPalMerchantId={MerchantId}",
                    page != null, page?.BusinessId, page?.Business?.OwnerId, page?.Business?.Owner?.PayPalMerchantId);

                if (page?.Business?.Owner != null)
                {
                    if (string.IsNullOrEmpty(page.Business.Owner.PayPalMerchantId))
                        return Results.NotFound(new { error = "Seller has not configured PayPal Merchant ID" });

                    return Results.Ok(new { merchantId = page.Business.Owner.PayPalMerchantId });
                }
            }

            logger.LogWarning("Could not find merchant ID for products: {Ids}", string.Join(", ", productIds));
            return Results.NotFound(new { error = "Merchant ID not found for these products. Please ensure the seller has configured their PayPal account." });
        });
    }
}
