using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PetitShope.Models;
using PetitShope.Dtos;

namespace PetitShope.Endpoints;

public static class PageEndpoints
{
    public static void MapPageEndpoints(this WebApplication app)
    {
        app.MapPost("/api/pages", async ([FromBody] Page page, AppDbContext db, ILogger<Program> logger, HttpRequest req, ClaimsPrincipal user) =>
        {
            try
            {
                // Log authorization header and user claims for debugging
                var authHeader = req.Headers["Authorization"].FirstOrDefault();
                logger.LogInformation("POST /api/pages Authorization: {AuthHeader}", authHeader ?? "(none)");
                if (user?.Identity?.IsAuthenticated == true)
                {
                    var claims = string.Join(",", user.Claims.Select(c => c.Type + ":" + c.Value));
                    logger.LogInformation("Authenticated user claims: {Claims}", claims);
                }
                // If page already exists, return 409 Conflict instead of causing a DB exception
                if (await db.Pages.AnyAsync(p => p.Id == page.Id))
                {
                    logger.LogInformation("Page {Id} already exists, skipping create", page.Id);
                    return Results.Conflict(new { error = $"Page {page.Id} already exists" });
                }

                // Validate BusinessId exists to avoid FK violations
                var businessExists = await db.Businesses.AnyAsync(b => b.Id == page.BusinessId);
                if (!businessExists)
                {
                    logger.LogWarning("Attempted to create page for non-existent BusinessId {BusinessId}", page.BusinessId);
                    return Results.BadRequest(new { error = $"BusinessId {page.BusinessId} does not exist" });
                }

                db.Pages.Add(page);
                var changed = await db.SaveChangesAsync();
                logger.LogInformation("Saved page {Id} ({Title}) with {Changes} changes", page.Id, page.Title, changed);
                var saved = await db.Pages.Where(p => p.Id == page.Id).Select(p => new PageDto
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
                }).FirstOrDefaultAsync();
                return Results.Created($"/api/pages/{page.Id}", saved ?? new PageDto { Id = page.Id, Title = page.Title, BusinessId = page.BusinessId });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save page");
                var detail = ex.InnerException?.Message ?? ex.Message;
                return Results.Problem(detail);
            }
        }).RequireAuthorization("SellerOnly");

        app.MapPut("/api/pages/{id}", async (string id, [FromBody] Page updated, AppDbContext db) =>
        {
            var existing = await db.Pages.FindAsync(id);
            if (existing == null) return Results.NotFound();
            existing.Title = updated.Title;
            existing.BusinessId = updated.BusinessId;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("SellerOnly");

        app.MapDelete("/api/pages/{id}", async (string id, AppDbContext db) =>
        {
            var existing = await db.Pages.FindAsync(id);
            if (existing == null) return Results.NotFound();
            db.Pages.Remove(existing);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("SellerOnly");
    }
}
