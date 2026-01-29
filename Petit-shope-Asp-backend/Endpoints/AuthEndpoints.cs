using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PetitShope.Models;
using PetitShope.Services;
using System.Text.Json;

namespace PetitShope.Endpoints;

public static class AuthEndpoints
{
    // Helper method to call Express email service
    private static async Task<bool> SendVerificationEmail(int userId, string email, string role, string name, ILogger logger, IConfiguration cfg)
    {
        try
        {
            using var httpClient = new HttpClient();
            var expressUrl = cfg["Express:Url"] ?? "http://localhost:4000";
            expressUrl = expressUrl.TrimEnd('/') + "/api/email/send-verification-email";

            var payload = new
            {
                userId = userId,
                email = email,
                role = role,
                name = name
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await httpClient.PostAsync(expressUrl, content);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("✅ Verification email sent to {Email} via Express service", email);
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("❌ Failed to send verification email: {Error}", errorContent);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error calling Express email service");
            return false;
        }
    }

    // Helper method to send password reset email via Express service
    private static async Task<bool> SendPasswordResetEmail(int userId, string email, string name, string resetToken, ILogger logger, IConfiguration cfg)
    {
        try
        {
            using var httpClient = new HttpClient();
            var expressUrl = cfg["Express:Url"] ?? "http://localhost:4000";
            var frontendUrl = cfg["Frontend:Url"] ?? "http://localhost:5173";

            // Build reset URL that frontend will handle
            var resetUrl = $"{frontendUrl}/auth/reset-password?token={resetToken}";

            var endpoint = expressUrl.TrimEnd('/') + "/api/email/send-password-reset-email";

            var payload = new
            {
                userId = userId,
                email = email,
                name = name,
                resetUrl = resetUrl
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await httpClient.PostAsync(endpoint, content);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("✅ Password reset email sent to {Email} via Express service", email);
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("❌ Failed to send password reset email: {Error}", errorContent);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error calling Express email service for password reset");
            return false;
        }
    }

    private static bool IsSmtpEnabled(IConfiguration cfg)
    {
        var raw = cfg["Smtp:Enabled"] ?? string.Empty;
        raw = raw.Trim().Trim('"', '\'');
        return bool.TryParse(raw, out var parsed) && parsed;
    }

    private static string HashPassword(string password)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password ?? "");
        var hash = sha.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static string GenerateVerificationToken(int size = 32)
    {
        var bytes = new byte[size];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        // URL-safe base64
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/buyer/register", async ([FromBody] RegisterDto dto, AppDbContext db, IConfiguration cfg, ILogger<Program> logger, HttpRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
                return Results.BadRequest(new { error = "Email and password required" });

            // Check if user already exists
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email && u.Role == "Buyer");
            if (existing != null)
            {
                if (existing.EmailVerified && existing.AccountStatus == "Verified")
                {
                    return Results.Conflict(new { error = "Email already registered as a buyer" });
                }

                // Not verified: resend verification via Express service
                var emailSent = await SendVerificationEmail(existing.Id, existing.Email, existing.Role, existing.Name, logger, cfg);

                return Results.Ok(new
                {
                    message = "Verification email has been resent. Please check your inbox.",
                    emailSent = emailSent
                });
            }

            // Create new user
            var user = new User
            {
                Name = dto.Name ?? dto.Email,
                Email = dto.Email,
                PasswordHash = HashPassword(dto.Password),
                Role = "Buyer",
                EmailVerified = false,
                AccountStatus = "Pending" // User must verify email before login
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            // Send verification email via Express service
            var emailSuccess = await SendVerificationEmail(user.Id, user.Email, user.Role, user.Name, logger, cfg);

            if (!emailSuccess)
            {
                logger.LogWarning("⚠️ User registered but verification email failed to send");
            }

            return Results.Created($"/api/auth/buyer/{user.Id}", new
            {
                user.Id,
                user.Email,
                message = "Registration successful. Please check your email to verify your account.",
                emailSent = emailSuccess
            });
        });

        app.MapPost("/api/auth/buyer/login", async ([FromBody] LoginDto dto, AppDbContext db, IConfiguration cfg, ILogger<Program> logger) =>
        {
            try
            {
                var hash = HashPassword(dto.Password);
                var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email && u.PasswordHash == hash && u.Role == "Buyer");

                if (user == null) return Results.Unauthorized();

                // Check if email is verified
                if (user.AccountStatus == "Pending" || !user.EmailVerified)
                {
                    return Results.Json(
                        new { error = "Please verify your email before logging in. Check your inbox for the verification link." },
                        statusCode: 403
                    );
                }

                // Generate JWT token
                var key = cfg["Jwt:Key"] ?? "dev_secret_key_change_me";
                var issuer = cfg["Jwt:Issuer"] ?? "petitshop";
                var claims = new[] {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim(ClaimTypes.Name, user.Name ?? "")
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                var keyBytes = Encoding.UTF8.GetBytes(key ?? "");
                if (keyBytes.Length < 32)
                {
                    keyBytes = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(key ?? ""));
                }
                var signingKey = new SymmetricSecurityKey(keyBytes);

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = DateTime.UtcNow.AddDays(7),
                    Issuer = issuer,
                    Audience = issuer,
                    SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
                };

                var token = tokenHandler.CreateToken(tokenDescriptor);
                var jwt = tokenHandler.WriteToken(token);

                return Results.Ok(new { token = jwt, user = new { user.Id, user.Email, user.Name } });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Buyer login failed");
                return Results.Problem(ex.Message);
            }
        });

        app.MapPost("/api/auth/seller/register", async ([FromBody] RegisterDto dto, AppDbContext db, IConfiguration cfg, ILogger<Program> logger, HttpRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
                return Results.BadRequest(new { error = "Email and password required" });

            // Check if seller already exists
            var sellerUser = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email && u.Role == "Seller");
            if (sellerUser != null)
            {
                if (sellerUser.EmailVerified && sellerUser.AccountStatus == "Verified")
                {
                    return Results.Conflict(new { error = "Email already registered as a seller" });
                }

                // Not verified: resend verification via Express service
                var emailSent = await SendVerificationEmail(sellerUser.Id, sellerUser.Email, sellerUser.Role, sellerUser.Name, logger, cfg);

                return Results.Ok(new
                {
                    message = "Verification email has been resent. Please check your inbox.",
                    emailSent = emailSent
                });
            }

            // Create new seller
            var user = new User
            {
                Name = dto.Name ?? dto.Email,
                Email = dto.Email,
                PasswordHash = HashPassword(dto.Password),
                Role = "Seller",
                EmailVerified = false,
                AccountStatus = "Pending"
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            // Send verification email via Express service
            var emailSuccess = await SendVerificationEmail(user.Id, user.Email, user.Role, user.Name, logger, cfg);

            if (!emailSuccess)
            {
                logger.LogWarning("⚠️ Seller registered but verification email failed to send");
            }

            return Results.Created($"/api/auth/seller/{user.Id}", new
            {
                user.Id,
                user.Email,
                message = "Registration successful. Please check your email to verify your account.",
                emailSent = emailSuccess
            });
        });

        app.MapPost("/api/auth/seller/login", async ([FromBody] LoginDto dto, AppDbContext db, IConfiguration cfg, ILogger<Program> logger) =>
        {
            try
            {
                var hash = HashPassword(dto.Password);
                var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email && u.PasswordHash == hash && u.Role == "Seller");

                if (user == null) return Results.Unauthorized();

                // Check if email is verified
                if (user.AccountStatus == "Pending" || !user.EmailVerified)
                {
                    return Results.Json(
                        new { error = "Please verify your email before logging in. Check your inbox for the verification link." },
                        statusCode: 403
                    );
                }

                // Generate JWT token
                var key = cfg["Jwt:Key"] ?? "dev_secret_key_change_me";
                var issuer = cfg["Jwt:Issuer"] ?? "petitshop";
                var claims = new[] {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim(ClaimTypes.Name, user.Name ?? "")
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                var keyBytes = Encoding.UTF8.GetBytes(key ?? "");
                if (keyBytes.Length < 32)
                {
                    keyBytes = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(key ?? ""));
                }
                var signingKey = new SymmetricSecurityKey(keyBytes);

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = DateTime.UtcNow.AddDays(7),
                    Issuer = issuer,
                    Audience = issuer,
                    SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
                };

                var token = tokenHandler.CreateToken(tokenDescriptor);
                var jwt = tokenHandler.WriteToken(token);

                return Results.Ok(new { token = jwt, user = new { user.Id, user.Email, user.Name } });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Seller login failed");
                return Results.Problem(ex.Message);
            }
        });

        // Update buyer profile (authenticated)
        app.MapPut("/api/auth/buyer/profile", async ([FromBody] ProfileDto dto, AppDbContext db, ClaimsPrincipal user) =>
        {
            var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idClaim, out var userId)) return Results.Unauthorized();

            var u = await db.Users.FindAsync(userId);
            if (u == null) return Results.NotFound();

            var role = user.FindFirst(ClaimTypes.Role)?.Value ?? "";
            if (role != "Buyer") return Results.Forbid();

            if (!string.IsNullOrWhiteSpace(dto.Email)) u.Email = dto.Email;
            if (!string.IsNullOrWhiteSpace(dto.Name)) u.Name = dto.Name;

            await db.SaveChangesAsync();

            // Note: PictureUrl is not yet persisted in the DB; echo it back so frontend can update local state.
            return Results.Ok(new { u.Id, u.Email, u.Name, pictureUrl = dto.PictureUrl ?? "" });
        }).RequireAuthorization();

        // Verify email token via Express service
        app.MapGet("/api/auth/verify-email", async (HttpContext context, AppDbContext db, ILogger<Program> logger, IConfiguration cfg) =>
        {
            var token = context.Request.Query["token"].ToString();

            if (string.IsNullOrWhiteSpace(token))
            {
                return Results.BadRequest("Verification token is required");
            }

            try
            {
                // Call Express service to validate token
                using var httpClient = new HttpClient();
                var expressUrl = cfg["Express:Url"] ?? "http://localhost:4000";
                var validateUrl = $"{expressUrl.TrimEnd('/')}/api/email/verify-token/{token}";
                var response = await httpClient.GetAsync(validateUrl);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger.LogWarning("Token validation failed: {Error}", errorContent);

                    // Return HTML page with error
                    var frontendUrl = cfg["Frontend:Url"] ?? "http://localhost:5173";
                    return Results.Content($@"
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <title>Email Verification Failed</title>
                            <style>
                                body {{ font-family: Arial, sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); }}
                                .container {{ background: white; padding: 40px; border-radius: 10px; text-align: center; max-width: 500px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); }}
                                h1 {{ color: #e74c3c; margin-bottom: 20px; }}
                                p {{ color: #666; line-height: 1.6; }}
                                a {{ color: #667eea; text-decoration: none; font-weight: bold; }}
                            </style>
                        </head>
                        <body>
                            <div class='container'>
                                <h1>❌ Verification Failed</h1>
                                <p>This verification link is invalid or has expired.</p>
                                <p>Please request a new verification email or contact support.</p>
                                <p><a href='{frontendUrl}'>Return to Home</a></p>
                            </div>
                        </body>
                        </html>
                    ", "text/html");
                }

                var tokenData = await response.Content.ReadFromJsonAsync<TokenValidationResponse>();

                if (tokenData == null || !tokenData.Valid)
                {
                    var frontendUrl = cfg["Frontend:Url"] ?? "http://localhost:5173";
                    return Results.Content($@"
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <title>Email Verification Failed</title>
                            <style>
                                body {{ font-family: Arial, sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); }}
                                .container {{ background: white; padding: 40px; border-radius: 10px; text-align: center; max-width: 500px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); }}
                                h1 {{ color: #e74c3c; margin-bottom: 20px; }}
                                p {{ color: #666; line-height: 1.6; }}
                                a {{ color: #667eea; text-decoration: none; font-weight: bold; }}
                            </style>
                        </head>
                        <body>
                            <div class='container'>
                                <h1>❌ Invalid Token</h1>
                                <p>The verification token is invalid or has expired.</p>
                                <p><a href='{frontendUrl}'>Return to Home</a></p>
                            </div>
                        </body>
                        </html>
                    ", "text/html");
                }

                // Find user and update verification status
                var user = await db.Users.FindAsync(tokenData.UserId);

                if (user == null)
                {
                    logger.LogError("User not found: {UserId}", tokenData.UserId);
                    return Results.NotFound("User not found");
                }

                // Update user status
                user.EmailVerified = true;
                user.AccountStatus = "Verified";
                user.EmailVerificationToken = null;
                user.EmailVerificationExpires = null;

                await db.SaveChangesAsync();

                logger.LogInformation("✅ Email verified for user {UserId} ({Email})", user.Id, user.Email);

                // Clear token from Express service
                var clearTokenPayload = new { token = token };
                var clearContent = new StringContent(
                    JsonSerializer.Serialize(clearTokenPayload),
                    Encoding.UTF8,
                    "application/json"
                );
                var clearUrl = $"{expressUrl.TrimEnd('/')}/api/email/clear-token";
                await httpClient.PostAsync(clearUrl, clearContent);

                // Return success HTML page
                var frontendLoginUrl = cfg["Frontend:Url"] ?? "http://localhost:5173";
                // Frontend uses '/auth/seller' and '/auth/buyer' as the login entry points
                frontendLoginUrl = frontendLoginUrl.TrimEnd('/') + (user.Role == "Seller" ? "/auth/seller" : "/auth/buyer");

                return Results.Content($@"
                    <!DOCTYPE html>
                    <html>
                    <head>
                        <title>Email Verified Successfully</title>
                        <style>
                            body {{ font-family: Arial, sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); }}
                            .container {{ background: white; padding: 40px; border-radius: 10px; text-align: center; max-width: 500px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); }}
                            h1 {{ color: #27ae60; margin-bottom: 20px; }}
                            p {{ color: #666; line-height: 1.6; }}
                            .button {{ display: inline-block; margin-top: 20px; padding: 12px 30px; background: #667eea; color: white; text-decoration: none; border-radius: 5px; font-weight: bold; }}
                            .button:hover {{ background: #5568d3; }}
                        </style>
                    </head>
                    <body>
                        <div class='container'>
                            <h1>✅ Email Verified!</h1>
                            <p>Your email has been successfully verified.</p>
                            <p>You can now log in to your account as a <strong>{user.Role}</strong>.</p>
                            <a href='{frontendLoginUrl}' class='button'>Go to Login</a>
                        </div>
                    </body>
                    </html>
                ", "text/html");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during email verification");
                return Results.Problem("An error occurred during verification");
            }
        });

        // OLD verify endpoint - keeping for backwards compatibility but deprecated
        app.MapGet("/api/auth/verify", async (string token, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(token)) return Results.BadRequest(new { error = "Token required" });
            var user = await db.Users.FirstOrDefaultAsync(u => u.EmailVerificationToken == token);
            if (user == null) return Results.NotFound(new { error = "Invalid token" });
            if (user.EmailVerificationExpires.HasValue && user.EmailVerificationExpires.Value < DateTime.UtcNow) return Results.BadRequest(new { error = "Token expired" });
            user.EmailVerified = true;
            user.AccountStatus = "Verified";
            user.EmailVerificationToken = null;
            user.EmailVerificationExpires = null;
            await db.SaveChangesAsync();
            return Results.Ok(new { message = "Email verified" });
        });

        // Resend verification token
        app.MapPost("/api/auth/resend-verification", async ([FromBody] ResendDto dto, AppDbContext db, IConfiguration cfg, ILogger<Program> logger, IEmailSender emailSender, HttpRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Role)) return Results.BadRequest(new { error = "Email and role required" });
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email && u.Role == dto.Role);
            if (user == null) return Results.NotFound(new { error = "User not found" });
            if (user.EmailVerified) return Results.Ok(new { message = "Already verified" });
            var token = GenerateVerificationToken();
            user.EmailVerificationToken = token;
            user.EmailVerificationExpires = DateTime.UtcNow.AddDays(1);
            await db.SaveChangesAsync();
            var baseUrl = cfg["App:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = $"{req.Scheme}://{req.Host.Value}";
            }
            var verifyUrl = baseUrl.TrimEnd('/') + $"/api/auth/verify?token={token}";
            logger.LogInformation("Resent verification token for {email}: {token}", user.Email, token);
            try
            {
                await emailSender.SendEmailAsync(user.Email, "Verify your PetitShope account", $"Please verify your email by visiting: <a href='{verifyUrl}'>{verifyUrl}</a>");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send verification email to {email}", user.Email);
            }
            var includeTokenInResponse = cfg["Smtp:Enabled"] != "true";
            return Results.Ok(includeTokenInResponse ? new { verifyUrl, token } : new { verifyUrl });
        });

        // Update seller profile (authenticated, seller role required)
        app.MapPut("/api/auth/seller/profile", async ([FromBody] ProfileDto dto, AppDbContext db, ClaimsPrincipal user) =>
        {
            var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idClaim, out var userId)) return Results.Unauthorized();

            var u = await db.Users.FindAsync(userId);
            if (u == null) return Results.NotFound();

            var role = user.FindFirst(ClaimTypes.Role)?.Value ?? "";
            if (role != "Seller") return Results.Forbid();

            if (!string.IsNullOrWhiteSpace(dto.Email)) u.Email = dto.Email;
            if (!string.IsNullOrWhiteSpace(dto.Name)) u.Name = dto.Name;

            await db.SaveChangesAsync();

            // PictureUrl not persisted yet; return it so frontend can update localStorage
            return Results.Ok(new { u.Id, u.Email, u.Name, pictureUrl = dto.PictureUrl ?? "" });
        }).RequireAuthorization();

        // Set PayPal Merchant ID for seller
        app.MapPut("/api/auth/seller/paypal-merchant", async (
            [FromBody] PayPalMerchantDto dto,
            AppDbContext db,
            ClaimsPrincipal user,
            ILogger<Program> logger
        ) =>
        {
            if (user?.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            var userId = int.Parse(user.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var u = await db.Users.FindAsync(userId);
            if (u == null || u.Role != "Seller")
            {
                return Results.BadRequest(new { error = "Only sellers can set PayPal Merchant ID" });
            }

            if (string.IsNullOrWhiteSpace(dto.PayPalMerchantId))
            {
                return Results.BadRequest(new { error = "PayPal Merchant ID is required" });
            }

            u.PayPalMerchantId = dto.PayPalMerchantId.Trim();
            await db.SaveChangesAsync();

            logger.LogInformation("Seller {SellerId} updated PayPal Merchant ID", userId);

            return Results.Ok(new
            {
                message = "PayPal Merchant ID updated successfully",
                payPalMerchantId = u.PayPalMerchantId
            });
        }).RequireAuthorization();

        // Get seller's PayPal Merchant ID
        app.MapGet("/api/auth/seller/paypal-merchant", async (
            AppDbContext db,
            ClaimsPrincipal user
        ) =>
        {
            if (user?.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            var userId = int.Parse(user.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var u = await db.Users.FindAsync(userId);
            if (u == null)
                return Results.NotFound();

            return Results.Ok(new { payPalMerchantId = u.PayPalMerchantId ?? "" });
        }).RequireAuthorization();

        // Forgot Password - sends reset email
        app.MapPost("/api/auth/forgot-password", async (
            [FromBody] ForgotPasswordDto dto,
            AppDbContext db,
            IConfiguration cfg,
            ILogger<Program> logger
        ) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Email))
                return Results.BadRequest(new { error = "Email is required" });

            // Find user by email (regardless of role, or filter by dto.Role if provided)
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

            // Don't reveal if email exists for security (always return success)
            if (user == null)
            {
                logger.LogWarning("Password reset requested for non-existent email: {Email}", dto.Email);
                return Results.Ok(new { message = "If that email exists, a password reset link has been sent." });
            }

            // Generate reset token (URL-safe, 48 bytes)
            var resetToken = GenerateVerificationToken(48);

            // Store token in database (reusing EmailVerificationToken field for password reset)
            user.EmailVerificationToken = resetToken;
            user.EmailVerificationExpires = DateTime.UtcNow.AddHours(2); // 2-hour expiration
            await db.SaveChangesAsync();

            // Send password reset email via Express service
            var emailSent = await SendPasswordResetEmail(user.Id, user.Email, user.Name, resetToken, logger, cfg);

            if (!emailSent)
            {
                logger.LogError("Failed to send password reset email to {Email}", user.Email);
            }

            // Always return success message (don't reveal if email was actually sent)
            return Results.Ok(new
            {
                message = "If that email exists, a password reset link has been sent.",
                emailSent = emailSent // For debugging, remove in production
            });
        });

        // Reset Password - validates token and updates password
        app.MapPost("/api/auth/reset-password", async (
            [FromBody] ResetPasswordDto dto,
            AppDbContext db,
            ILogger<Program> logger
        ) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return Results.BadRequest(new { error = "Token and new password are required" });

            // Find user by reset token
            var user = await db.Users.FirstOrDefaultAsync(u => u.EmailVerificationToken == dto.Token);

            if (user == null)
            {
                logger.LogWarning("Password reset attempted with invalid token");
                return Results.BadRequest(new { error = "Invalid or expired reset token" });
            }

            // Check token expiration
            if (user.EmailVerificationExpires == null || user.EmailVerificationExpires < DateTime.UtcNow)
            {
                logger.LogWarning("Password reset attempted with expired token for user {UserId}", user.Id);
                return Results.BadRequest(new { error = "Reset token has expired. Please request a new password reset." });
            }

            // Validate password strength (basic check)
            if (dto.NewPassword.Length < 6)
            {
                return Results.BadRequest(new { error = "Password must be at least 6 characters long" });
            }

            // Update password
            user.PasswordHash = HashPassword(dto.NewPassword);

            // Clear reset token
            user.EmailVerificationToken = null;
            user.EmailVerificationExpires = null;

            await db.SaveChangesAsync();

            logger.LogInformation("Password successfully reset for user {UserId}", user.Id);

            return Results.Ok(new { message = "Password has been reset successfully. You can now login with your new password." });
        });
    }

    // DTO for token validation response from Express
    public record TokenValidationResponse(bool Valid, int UserId, string Email, string Role);

    public record PayPalMerchantDto(string PayPalMerchantId);
    public record ProfileDto(string? Name, string? Email, string? PictureUrl);
    public record RegisterDto(string? Name, string Email, string Password);
    public record LoginDto(string Email, string Password);
    public record ResendDto(string Email, string Role);
    public record ForgotPasswordDto(string Email, string? Role);
    public record ResetPasswordDto(string Token, string NewPassword);
}
