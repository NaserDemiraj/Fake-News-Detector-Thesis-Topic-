using FakeNewsDetector.Models;
using FakeNewsDetector.Services;
using Microsoft.AspNetCore.Mvc;

namespace FakeNewsDetector.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Problem(detail: "Name is required.", statusCode: 400);
            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
                return Problem(detail: "A valid email address is required.", statusCode: 400);
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                return Problem(detail: "Password must be at least 8 characters.", statusCode: 400);

            try
            {
                var result = await _authService.RegisterAsync(request);
                return Ok(result);
            }
            catch (InvalidOperationException ex) when (ex.Message == "EMAIL_TAKEN")
            {
                return Problem(detail: "This email is already registered.", statusCode: 409);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                return Problem(detail: "Registration failed. Please try again.", statusCode: 500);
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Problem(detail: "Email and password are required.", statusCode: 400);

            try
            {
                var result = await _authService.LoginAsync(request);
                if (result == null)
                    return Problem(detail: "Invalid email or password.", statusCode: 401);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return Problem(detail: "Login failed. Please try again.", statusCode: 500);
            }
        }
    }
}
