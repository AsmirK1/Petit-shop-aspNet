namespace PetitShope.Models;

public class User
{
    public int Id { get; set; }          // Primary Key
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    // hashed password (stored as hex string)
    public string PasswordHash { get; set; } = "";
    // Role: "Buyer" or "Seller"
    public string Role { get; set; } = "Buyer";

    // Email verification
    public bool EmailVerified { get; set; } = false;
    public string? EmailVerificationToken { get; set; }
    public DateTime? EmailVerificationExpires { get; set; }

    // Account status: "Pending" (awaiting verification) or "Verified" (active)
    public string AccountStatus { get; set; } = "Pending";

    // PayPal Merchant ID (for sellers)
    public string? PayPalMerchantId { get; set; }
}
