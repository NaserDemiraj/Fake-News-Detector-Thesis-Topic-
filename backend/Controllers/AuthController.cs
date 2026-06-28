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

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return Problem(detail: "Refresh token is required.", statusCode: 400);

            try
            {
                var result = await _authService.RefreshAsync(request.RefreshToken);
                if (result == null)
                    return Problem(detail: "Invalid or expired refresh token.", statusCode: 401);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");
                return Problem(detail: "Could not refresh session.", statusCode: 500);
            }
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
        {
            try
            {
                await _authService.LogoutAsync(request.RefreshToken);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return NoContent(); // logout should never hard-fail the client
            }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return Problem(detail: "Email is required.", statusCode: 400);

            try
            {
                await _authService.ForgotPasswordAsync(request.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during forgot-password");
            }
            // Always 200 — never reveal whether the email exists
            return Ok(new { message = "If an account exists for that email, a reset link has been sent." });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return Problem(detail: "Reset token is required.", statusCode: 400);
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
                return Problem(detail: "Password must be at least 8 characters.", statusCode: 400);

            try
            {
                var ok = await _authService.ResetPasswordAsync(request.Token, request.NewPassword);
                if (!ok)
                    return Problem(detail: "This reset link is invalid or has expired.", statusCode: 400);
                return Ok(new { message = "Password updated. You can now sign in." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during password reset");
                return Problem(detail: "Could not reset password.", statusCode: 500);
            }
        }

        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return Problem(detail: "Verification token is required.", statusCode: 400);

            try
            {
                var ok = await _authService.VerifyEmailAsync(token);
                if (!ok)
                    return Problem(detail: "This verification link is invalid or has expired.", statusCode: 400);
                return Ok(new { message = "Email verified successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during email verification");
                return Problem(detail: "Could not verify email.", statusCode: 500);
            }
        }
    }
}
