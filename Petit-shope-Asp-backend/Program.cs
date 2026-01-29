using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetitShope.Models;
using Microsoft.Extensions.DependencyInjection;
using PetitShope.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Enable CORS for development (allow all origins, methods, headers)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// Add authentication/authorization
var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev_secret_key_change_me";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "petitshop";
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtIssuer,
        // Derive a 256-bit signing key when the configured key is too short,
        // matching the logic used when issuing tokens.
        IssuerSigningKey = CreateSigningKey(jwtKey)
    };
});

static SymmetricSecurityKey CreateSigningKey(string key)
{
    var keyBytes = Encoding.UTF8.GetBytes(key ?? "");
    if (keyBytes.Length < 32)
    {
        keyBytes = System.Security.Cryptography.SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(key ?? ""));
    }
    return new SymmetricSecurityKey(keyBytes);
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SellerOnly", policy => policy.RequireRole("Seller"));
});

// Add services to the container.
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Register SMTP email sender (MailKit implementation)
builder.Services.AddSingleton<PetitShope.Services.IEmailSender, PetitShope.Services.MailKitEmailSender>();

var app = builder.Build();

// Use CORS policy
app.UseCors("AllowAll");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Show detailed error page in development to aid debugging
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// enable authentication/authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Ensure database is migrated on startup (creates tables if missing) and log connection
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var conn = config.GetConnectionString("DefaultConnection") ?? "(none)";
    logger.LogInformation("Using DB connection: {Conn}", conn);
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration failed");
    }

    // Check SMTP configuration and warn if enabled but appears incomplete
    try
    {
        var smtpEnabledRaw = config["Smtp:Enabled"] ?? "(null)";
        logger.LogInformation("Smtp:Enabled raw value: {Val}", smtpEnabledRaw);
        var smtpEnabled = smtpEnabledRaw == "true";
        if (smtpEnabled)
        {
            var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var host = cfg["Smtp:Host"] ?? "";
            var port = cfg["Smtp:Port"] ?? "";
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(port))
            {
                logger.LogWarning("SMTP is enabled but Smtp:Host or Smtp:Port is not configured. Emails may fail to send.");
            }
            else
            {
                logger.LogInformation("SMTP enabled using host {Host}:{Port}", host, port);
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to validate SMTP configuration");
    }

    try
    {
        // Identify businesses that are not wired to any seller.
        // Previously we deleted these automatically on startup which can remove
        // legitimate records when users are missing (e.g. during restore/import).
        // Change behavior: only delete when explicitly enabled via configuration.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orphaned = db.Businesses
            .Where(b => b.OwnerId == null || !db.Users.Any(u => u.Id == b.OwnerId))
            .ToList();
        if (orphaned.Any())
        {
            logger.LogWarning("Found {Count} orphaned businesses (missing owner).", orphaned.Count);
            // Log details to help manual inspection
            foreach (var b in orphaned)
            {
                logger.LogWarning("Orphaned business: Id={Id}, Name={Name}, City={City}, Country={Country}", b.Id, b.Name, b.City, b.Country);
            }

            // Only delete if explicitly allowed by configuration key "Cleanup:DeleteOrphanedBusinesses" == "true"
            var deleteRaw = config["Cleanup:DeleteOrphanedBusinesses"] ?? "false";
            var doDelete = string.Equals(deleteRaw, "true", StringComparison.OrdinalIgnoreCase);
            if (doDelete)
            {
                logger.LogInformation("Deleting orphaned businesses as configured.");
                db.Businesses.RemoveRange(orphaned);
                db.SaveChanges();
                logger.LogInformation("Removed orphaned businesses.");
            }
            else
            {
                logger.LogInformation("Skipping deletion of orphaned businesses. To enable deletion set Cleanup:DeleteOrphanedBusinesses=true in configuration.");
            }
        }
    }
    catch (Exception ex)
    {
        var logger2 = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger2.LogError(ex, "Failed to identify or cleanup orphaned businesses");
    }
}

// Map endpoints from separate files
app.MapBusinessEndpoints();
app.MapProductEndpoints();
app.MapPageEndpoints();
app.MapCartItemEndpoints();
app.MapOrderEndpoints();
app.MapAuthEndpoints();

// Simple health endpoint
app.MapGet("/", () => Results.Ok(new { status = "ok" }));


app.Run();
