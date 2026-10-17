using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AuthService.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AuthDbContext _db;
        private readonly IConfiguration _config;

        public AuthController(AuthDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest req)
        {
            if (await _db.Users.AnyAsync(u => u.Email == req.Email))
                return Conflict(new { message = "Email already registered" });

            var user = new User
            {
                Email = req.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                Role = "User"
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return Ok(new { user.Id, user.Email, user.Role });
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
        {
            var user = await _db.Users.SingleOrDefaultAsync(u => u.Email == req.Email);
            if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
                return Unauthorized(new { message = "Invalid credentials" });

            var token = GenerateJwt(user);
            return Ok(new AuthResponse(token, user.Email, user.Role));
        }

        // Example RBAC-protected endpoint — only Admins can promote a user.
        [Authorize(Roles = "Admin")]
        [HttpPost("promote/{userId:guid}")]
        public async Task<IActionResult> Promote(Guid userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null) return NotFound();
            user.Role = "Admin";
            await _db.SaveChangesAsync();
            return Ok(new { user.Id, user.Role });
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var email = User.FindFirstValue(ClaimTypes.Email);
            var role = User.FindFirstValue(ClaimTypes.Role);
            return Ok(new { email, role });
        }

        private string GenerateJwt(User user)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
