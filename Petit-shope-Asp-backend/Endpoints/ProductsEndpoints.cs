using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetitShope.Models;
using PetitShope.Dtos;
using Microsoft.EntityFrameworkCore;

namespace PetitShope.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        app.MapGet("/api/products", async (AppDbContext db, string? category, int? businessId) =>
        {
            // Products from Products table
            var prodQuery = db.Products.AsQueryable();
            if (!string.IsNullOrEmpty(category)) prodQuery = prodQuery.Where(p => p.Category == category);
            if (businessId.HasValue) prodQuery = prodQuery.Where(p => p.BusinessId == businessId.Value);
            var products = await prodQuery.Select(p => new ViewProductDto
            {
                Id = p.Id.ToString(),
                Title = p.Title,
                Price = p.Price,
                Category = p.Category,
                Image = p.Image,
                BusinessId = p.BusinessId
            }).ToListAsync();

            // CartItems from Pages (these are the shop items stored on pages)
            var cartQuery = db.CartItems.AsQueryable();
            if (!string.IsNullOrEmpty(category)) cartQuery = cartQuery.Where(c => c.Category == category);
            if (businessId.HasValue)
            {
                // join through pages to filter by business
                cartQuery = cartQuery.Where(c => db.Pages.Any(p => p.Id == c.PageId && p.BusinessId == businessId.Value));
            }
            var carts = await cartQuery.Select(c => new ViewProductDto
            {
                Id = c.Id,
                Title = c.Title,
                Price = c.Price,
                Category = c.Category,
                Image = c.Image,
                BusinessId = db.Pages.Where(p => p.Id == c.PageId).Select(p => (int?)p.BusinessId).FirstOrDefault()
            }).ToListAsync();

            // combine (cart items first so they show up) — front-end groups by category
            var combined = carts.Concat(products).ToList();
            return combined;
        });

        app.MapPost("/api/products", async ([FromBody] Product product, AppDbContext db, ILogger<Program> logger) =>
        {
            try
            {
                db.Products.Add(product);
                var changed = await db.SaveChangesAsync();
                logger.LogInformation("Saved product {Title} with {Changes} changes", product.Title, changed);
                var saved = await db.Products.Where(p => p.Id == product.Id).Select(p => new ProductDto
                {
                    Id = p.Id,
                    Title = p.Title,
                    Price = p.Price,
                    Category = p.Category,
                    Image = p.Image,
                    BusinessId = p.BusinessId
                }).FirstOrDefaultAsync();
                return Results.Created($"/api/products/{product.Id}", saved ?? new ProductDto { Id = product.Id, Title = product.Title, Price = product.Price, Category = product.Category, Image = product.Image, BusinessId = product.BusinessId });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save product");
                return Results.Problem(ex.Message);
            }
        }).RequireAuthorization("SellerOnly");
    }
}
