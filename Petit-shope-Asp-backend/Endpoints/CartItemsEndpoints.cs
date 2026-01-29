using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetitShope.Models;
using PetitShope.Dtos;

namespace PetitShope.Endpoints;

public static class CartItemEndpoints
{
    public static void MapCartItemEndpoints(this WebApplication app)
    {
        app.MapPost("/api/cartitems", async ([FromBody] CartItem cart, AppDbContext db, ILogger<Program> logger) =>
        {
            try
            {
                // Validate PageId exists to avoid FK violations
                if (!await db.Pages.AnyAsync(p => p.Id == cart.PageId))
                {
                    logger.LogWarning("Attempted to create CartItem for non-existent PageId {PageId}", cart.PageId);
                    return Results.BadRequest(new { error = $"PageId {cart.PageId} does not exist" });
                }

                // If an item with this Id already exists, return Conflict
                if (await db.CartItems.AnyAsync(c => c.Id == cart.Id))
                {
                    logger.LogInformation("CartItem {Id} already exists, returning Conflict", cart.Id);
                    return Results.Conflict(new { error = $"CartItem {cart.Id} already exists" });
                }

                db.CartItems.Add(cart);
                var changed = await db.SaveChangesAsync();
                logger.LogInformation("Saved cart {Id} with {Changes} changes", cart.Id, changed);
                var saved = await db.CartItems.Where(c => c.Id == cart.Id).Select(c => new CartItemDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Price = c.Price,
                    Category = c.Category,
                    Image = c.Image,
                    PageId = c.PageId
                }).FirstOrDefaultAsync();
                return Results.Created($"/api/cartitems/{cart.Id}", saved ?? new CartItemDto { Id = cart.Id, Title = cart.Title, Price = cart.Price, Category = cart.Category, Image = cart.Image, PageId = cart.PageId });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save cart item");
                var detail = ex.InnerException?.Message ?? ex.Message;
                return Results.Problem(detail);
            }
        }).RequireAuthorization("SellerOnly");

        app.MapPut("/api/cartitems/{id}", async (string id, [FromBody] CartItem updated, AppDbContext db, ILogger<Program> logger) =>
        {
            var existing = await db.CartItems.FindAsync(id);
            if (existing == null)
            {
                // Validate PageId exists before creating
                if (!await db.Pages.AnyAsync(p => p.Id == updated.PageId))
                {
                    logger.LogWarning("Attempted to create (via PUT) CartItem for non-existent PageId {PageId}", updated.PageId);
                    return Results.BadRequest(new { error = $"PageId {updated.PageId} does not exist" });
                }
                // Create new
                db.CartItems.Add(updated);
                await db.SaveChangesAsync();
                var saved = await db.CartItems.Where(c => c.Id == updated.Id).Select(c => new CartItemDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Price = c.Price,
                    Category = c.Category,
                    Image = c.Image,
                    PageId = c.PageId
                }).FirstOrDefaultAsync();
                return Results.Created($"/api/cartitems/{updated.Id}", saved);
            }
            existing.Title = updated.Title;
            existing.Price = updated.Price;
            existing.Category = updated.Category;
            existing.Image = updated.Image;
            existing.PageId = updated.PageId;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("SellerOnly");

        app.MapDelete("/api/cartitems/{id}", async (string id, AppDbContext db) =>
        {
            var existing = await db.CartItems.FindAsync(id);
            if (existing == null) return Results.NotFound();
            db.CartItems.Remove(existing);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("SellerOnly");
    }
}
