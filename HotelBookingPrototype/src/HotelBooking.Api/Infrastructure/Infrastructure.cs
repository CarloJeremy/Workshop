using System.Security.Cryptography;
using System.Text;

namespace HotelBooking.Api.Infrastructure;

public class JwtSettings
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Key { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 60;
}

public class OtpSettings
{
    public int TtlMinutes { get; set; } = 10;
    public int MaxAttempts { get; set; } = 3;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxResendPerHour { get; set; } = 5;
}

public class AuthSettings
{
    public int LockoutMinutes { get; set; } = 30;
}

public class BookingSettings
{
    public int ExpiryMinutes { get; set; } = 30;
    public int ExpiryScanSeconds { get; set; } = 30;
}

public class IntegrationSettings
{
    public string CallbackSecret { get; set; } = "";
}

/// <summary>Hashing password: SHA-256 + salt acak per user (prototipe). Produksi: Argon2id sesuai TSD 7.2.</summary>
public class PasswordHasher
{
    public static string HashStatic(string password) => HashCore(password);

    public string Hash(string password) => HashCore(password);

    public bool Verify(string password, string stored) => VerifyStatic(password, stored);

    private static string HashCore(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = SHA256.HashData(salt.Concat(Encoding.UTF8.GetBytes(password)).ToArray());
        return Convert.ToHexString(salt) + "$" + Convert.ToHexString(hash);
    }

    private static bool VerifyStatic(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 2) return false;
        var salt = Convert.FromHexString(parts[0]);
        var hash = SHA256.HashData(salt.Concat(Encoding.UTF8.GetBytes(password)).ToArray());
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(parts[1]));
    }
}

/// <summary>HMAC-SHA256 untuk signature callback Payment Gateway & token QR E-Ticket.</summary>
public static class HmacHelper
{
    public static string Sign(string payload, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static bool Verify(string payload, string secret, string signature)
    {
        var expected = Sign(payload, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes((signature ?? "").Trim().ToLowerInvariant()));
    }
}

/// <summary>Exception bisnis -> response envelope standar (TSD bagian 9.1).</summary>
public class BusinessException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public BusinessException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BusinessException ex)
        {
            _logger.LogWarning("Business error {Code}: {Message}", ex.Code, ex.Message);
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new { status = "FAILED", code = ex.Code, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { status = "FAILED", code = "INTERNAL_ERROR", message = "Terjadi kesalahan pada sistem." });
        }
    }
}

public static class ApiExtensions
{
    public static Guid CurrentUserId(this System.Security.Claims.ClaimsPrincipal user)
    {
        var sub = user.FindFirst("sub")?.Value
                  ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
