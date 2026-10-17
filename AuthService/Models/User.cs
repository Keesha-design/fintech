namespace AuthService.Models
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Email { get; set; } = default!;
        public string PasswordHash { get; set; } = default!;
        // Simple RBAC: "Admin" or "User". A real bank app would use a many-to-many roles table
        // plus a maker-checker flag for anyone allowed to approve high-value transactions.
        public string Role { get; set; } = "User";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public record RegisterRequest(string Email, string Password);
    public record LoginRequest(string Email, string Password);
    public record AuthResponse(string Token, string Email, string Role);
}
