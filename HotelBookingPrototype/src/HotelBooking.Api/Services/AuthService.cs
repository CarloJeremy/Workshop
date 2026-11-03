using HotelBooking.Api.Data;
using HotelBooking.Api.Dtos;
using HotelBooking.Api.Infrastructure;
using HotelBooking.Api.Models;

namespace HotelBooking.Api.Services;

public class OtpService
{
    private readonly InMemoryDb _db;
    private readonly OtpSettings _settings;
    private readonly NotificationService _notifications;

    public OtpService(InMemoryDb db, OtpSettings settings, NotificationService notifications)
    {
        _db = db;
        _settings = settings;
        _notifications = notifications;
    }

    public string Issue(Customer customer)
    {
        // Cooldown resend 60 detik
        var last = _db.Otps.Values.Where(o => o.CustomerId == customer.CustomerId).OrderByDescending(o => o.CreatedAt).FirstOrDefault();
        if (last != null && (DateTime.UtcNow - last.CreatedAt).TotalSeconds < _settings.ResendCooldownSeconds)
            throw new BusinessException(429, "RATE_LIMITED", "Tunggu sebentar sebelum meminta OTP baru.");

        // Batas maksimum penerbitan OTP per jam (UAT-REG-07 / UAT-SEC-03).
        var lastHour = _db.Otps.Values.Count(o => o.CustomerId == customer.CustomerId && o.CreatedAt > DateTime.UtcNow.AddHours(-1));
        if (lastHour >= _settings.MaxResendPerHour)
            throw new BusinessException(429, "TOO_MANY_REQUESTS", "Batas maksimum permintaan OTP per jam tercapai. Coba lagi nanti.");

        var otp = Random.Shared.Next(100000, 999999).ToString();
        var record = new OtpVerification
        {
            CustomerId = customer.CustomerId,
            OtpHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(otp))),
            ExpiresAt = DateTime.UtcNow.AddMinutes(_settings.TtlMinutes)
        };
        _db.Otps[record.OtpId] = record;
        _notifications.SendOtp(customer.Email, otp);
        return otp;
    }

    public void Verify(Customer customer, string otpCode)
    {
        var record = _db.Otps.Values
            .Where(o => o.CustomerId == customer.CustomerId && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefault();

        if (record == null)
            throw new BusinessException(400, "OTP_INVALID", "OTP tidak ditemukan. Silakan minta OTP baru.");
        if (record.ExpiresAt < DateTime.UtcNow)
            throw new BusinessException(410, "OTP_EXPIRED", "OTP sudah kedaluwarsa.");
        if (record.AttemptCount >= _settings.MaxAttempts)
            throw new BusinessException(429, "TOO_MANY_REQUESTS", "Terlalu banyak percobaan OTP salah. Silakan minta OTP baru.");

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(otpCode)));
        if (hash != record.OtpHash)
        {
            record.AttemptCount++;
            throw new BusinessException(401, "OTP_INVALID", "OTP salah.");
        }

        record.IsUsed = true;
    }
}

public class AuthService
{
    private readonly InMemoryDb _db;
    private readonly PasswordHasher _hasher;
    private readonly TokenService _tokens;
    private readonly OtpService _otps;
    private readonly AuthSettings _auth;

    public AuthService(InMemoryDb db, PasswordHasher hasher, TokenService tokens, OtpService otps, AuthSettings auth)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _otps = otps;
        _auth = auth;
    }

    public object Register(RegisterRequest req)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw new BusinessException(400, "VALIDATION_ERROR", "Format email tidak valid.");
        if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 8)
            throw new BusinessException(400, "VALIDATION_ERROR", "Password minimal 8 karakter.");
        if (_db.Customers.Values.Any(c => c.Email == email))
            throw new BusinessException(409, "EMAIL_ALREADY_REGISTERED", "Email sudah terdaftar.");

        var customer = new Customer
        {
            Email = email,
            PhoneNumber = req.PhoneNumber,
            FullName = req.FullName,
            PasswordHash = _hasher.Hash(req.Password),
            AccountStatus = AccountStatus.PendingVerification,
            Role = "Customer"
        };
        _db.Customers[customer.CustomerId] = customer;
        var otp = _otps.Issue(customer);
        _db.AddAudit("Customer", customer.CustomerId, "REGISTER", null, "PendingVerification", email);

        // Prototipe: email belum aktif, OTP dikembalikan agar dapat ditampilkan di UI.
        return new { customer_id = customer.CustomerId, account_status = "PENDING_VERIFICATION", dev_otp = otp };
    }

    public object VerifyOtp(OtpRequest req)
    {
        var customer = FindByEmail(req.Email);
        _otps.Verify(customer, req.OtpCode);
        customer.AccountStatus = AccountStatus.Active;
        _db.AddAudit("Customer", customer.CustomerId, "OTP_VERIFIED", "PendingVerification", "Active", customer.Email);
        return new { account_status = "ACTIVE" };
    }

    public object ResendOtp(ResendOtpRequest req)
    {
        var customer = FindByEmail(req.Email);
        if (customer.AccountStatus == AccountStatus.Active)
            throw new BusinessException(400, "VALIDATION_ERROR", "Akun sudah aktif.");
        var otp = _otps.Issue(customer);
        // Prototipe: email belum aktif, OTP dikembalikan agar dapat ditampilkan di UI.
        return new { message = "OTP baru telah dikirim.", dev_otp = otp };
    }

    public object Login(LoginRequest req)
    {
        var key = (req.EmailOrPhone ?? "").Trim().ToLowerInvariant();
        var customer = _db.Customers.Values.FirstOrDefault(c => c.Email == key || c.PhoneNumber == key)
            ?? throw new BusinessException(401, "INVALID_CREDENTIALS", "Email/password salah.");

        if (customer.AccountStatus == AccountStatus.Locked)
        {
            if (customer.LockedUntil > DateTime.UtcNow)
                throw new BusinessException(423, "ACCOUNT_LOCKED", $"Akun terkunci sampai {customer.LockedUntil:O}. Coba lagi nanti.");
            customer.AccountStatus = AccountStatus.Active;
            customer.FailedAttemptCount = 0;
            customer.LockedUntil = null;
        }
        if (customer.AccountStatus == AccountStatus.PendingVerification)
            throw new BusinessException(403, "ACCOUNT_NOT_VERIFIED", "Akun belum diverifikasi. Silakan verifikasi OTP.");
        if (customer.AccountStatus == AccountStatus.Blocked)
            throw new BusinessException(403, "ACCOUNT_BLOCKED", "Akun diblokir. Hubungi administrator.");

        if (!_hasher.Verify(req.Password ?? "", customer.PasswordHash))
        {
            customer.FailedAttemptCount++;
            if (customer.FailedAttemptCount >= 5)
            {
                customer.AccountStatus = AccountStatus.Locked;
                customer.LockedUntil = DateTime.UtcNow.AddMinutes(_auth.LockoutMinutes);
                _db.AddAudit("Customer", customer.CustomerId, "LOCKED", null, $"{_auth.LockoutMinutes} minutes", customer.Email);
            }
            throw new BusinessException(401, "INVALID_CREDENTIALS", "Email/password salah.");
        }

        customer.FailedAttemptCount = 0;
        customer.LastLoginAt = DateTime.UtcNow;

        var refreshValue = _tokens.NewRefreshTokenValue();
        var refresh = new RefreshToken
        {
            CustomerId = customer.CustomerId,
            TokenHash = _tokens.HashToken(refreshValue),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _db.RefreshTokens[refresh.Id] = refresh;

        return new
        {
            access_token = _tokens.CreateAccessToken(customer),
            expires_in = _tokens.AccessTokenSeconds,
            refresh_token = refreshValue,
            customer_id = customer.CustomerId,
            role = customer.Role
        };
    }

    public object Refresh(RefreshRequest req)
    {
        var hash = _tokens.HashToken(req.RefreshToken ?? "");
        var record = _db.RefreshTokens.Values.FirstOrDefault(t => t.TokenHash == hash)
            ?? throw new BusinessException(401, "INVALID_TOKEN", "Refresh token tidak valid.");
        if (record.IsRevoked || record.ExpiresAt < DateTime.UtcNow)
            throw new BusinessException(401, "INVALID_TOKEN", "Refresh token kedaluwarsa/dicabut.");

        record.IsRevoked = true; // rotating refresh token
        var customer = _db.Customers[record.CustomerId];
        var newValue = _tokens.NewRefreshTokenValue();
        var newRecord = new RefreshToken
        {
            CustomerId = customer.CustomerId,
            TokenHash = _tokens.HashToken(newValue),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _db.RefreshTokens[newRecord.Id] = newRecord;

        return new
        {
            access_token = _tokens.CreateAccessToken(customer),
            expires_in = _tokens.AccessTokenSeconds,
            refresh_token = newValue
        };
    }

    public object Logout(LogoutRequest? req, string? accessToken)
    {
        // Revoke refresh token bila dikirim.
        if (!string.IsNullOrEmpty(req?.RefreshToken))
        {
            var hash = _tokens.HashToken(req.RefreshToken);
            var record = _db.RefreshTokens.Values.FirstOrDefault(t => t.TokenHash == hash);
            if (record != null) record.IsRevoked = true;
        }

        // Revoke access token yang dipakai saat logout (jti blocklist) — sesi langsung tidak berlaku (UAT-LOG-06).
        if (!string.IsNullOrEmpty(accessToken))
        {
            var jti = TokenService.GetJti(accessToken);
            if (jti != null) _db.RevokedAccessJtis[jti] = DateTime.UtcNow;
        }
        return new { message = "Berhasil logout." };
    }

    private Customer FindByEmail(string? email)
    {
        var key = (email ?? "").Trim().ToLowerInvariant();
        return _db.Customers.Values.FirstOrDefault(c => c.Email == key)
            ?? throw new BusinessException(404, "NOT_FOUND", "Akun tidak ditemukan.");
    }
}
