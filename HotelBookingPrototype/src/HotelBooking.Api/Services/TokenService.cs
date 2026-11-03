using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HotelBooking.Api.Infrastructure;
using HotelBooking.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace HotelBooking.Api.Services;

public class TokenService
{
    private readonly JwtSettings _jwt;

    public TokenService(JwtSettings jwt) => _jwt = jwt;

    public string CreateAccessToken(Customer customer)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, customer.CustomerId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, customer.Email),
            new Claim("role", customer.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims,
            expires: DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public int AccessTokenSeconds => _jwt.AccessTokenMinutes * 60;

    public string NewRefreshTokenValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Ekstrak klaim jti dari access token (tanpa validasi signature) untuk mekanisme revokasi logout.</summary>
    public static string? GetJti(string accessToken)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
            return jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
        }
        catch { return null; }
    }
}

public class NotificationService
{
    private readonly Data.InMemoryDb _db;
    public NotificationService(Data.InMemoryDb db) => _db = db;

    // Prototipe: email tidak benar-benar dikirim, hanya dicatat (notification_log).
    // Simulasi UAT-TKT-05: penerima dengan kata "emailgagal" dianggap layanan email sandbox sedang gagal
    // — dicatat status FAILED + jadwal retry (backoff 1m, 5m, 15m; maksimum 3 kali).
    private static NotificationStatus Deliver(string recipient, out int retry, out string? lastError)
    {
        if (recipient.Contains("emailgagal", StringComparison.OrdinalIgnoreCase))
        {
            retry = 3;
            lastError = "SMTP sandbox gagal (simulasi). Retry dijadwalkan backoff 1m/5m/15m; setelah 3x gagal permanen + alert operasional.";
            return NotificationStatus.Failed;
        }
        retry = 0;
        lastError = null;
        return NotificationStatus.Sent;
    }

    public void SendOtp(string email, string otp)
    {
        var status = Deliver(email, out var retry, out var lastError);
        var log = new NotificationLog
        {
            Type = NotificationType.Otp,
            Recipient = email,
            Subject = "Kode OTP Registrasi",
            Status = status,
            RetryCount = retry,
            LastError = lastError
        };
        _db.Notifications[log.Id] = log;
        Console.WriteLine($"[EMAIL-OTP] to={email} otp={otp} status={status} (prototipe: OTP ditampilkan di log)");
    }

    public void SendETicket(string email, string bookingNumber, string qrCode)
    {
        var status = Deliver(email, out var retry, out var lastError);
        var log = new NotificationLog
        {
            Type = NotificationType.ETicket,
            Recipient = email,
            Subject = $"E-Ticket Booking {bookingNumber}",
            Status = status,
            RetryCount = retry,
            LastError = lastError
        };
        _db.Notifications[log.Id] = log;
        Console.WriteLine($"[EMAIL-ETICKET] to={email} booking={bookingNumber} qr={qrCode} status={status}");
    }
}
