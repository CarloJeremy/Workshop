namespace HotelBooking.Api.Models;

public class Customer
{
    public Guid CustomerId { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string? PhoneNumber { get; set; }
    public string PasswordHash { get; set; } = "";
    public string? FullName { get; set; }
    public AccountStatus AccountStatus { get; set; } = AccountStatus.PendingVerification;
    public string Role { get; set; } = "Customer";
    public int FailedAttemptCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class OtpVerification
{
    public Guid OtpId { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string OtpHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Hotel
{
    public string HotelId { get; set; } = "";
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Location { get; set; } = "";
    public double Rating { get; set; }
    public List<string> Facilities { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

public class Room
{
    public string RoomId { get; set; } = "";
    public string HotelId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Capacity { get; set; }
    public decimal PricePerNight { get; set; }
    public string Currency { get; set; } = "IDR";
    public int TotalRooms { get; set; }
    public string Policy { get; set; } = "Flexible"; // Flexible | NonRefundable
    public decimal PenaltyRate { get; set; } = 0.10m;
}

public class Booking
{
    public Guid BookingId { get; set; } = Guid.NewGuid();
    public string BookingNumber { get; set; } = "";
    public Guid CustomerId { get; set; }
    public string HotelId { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string HotelName { get; set; } = "";
    public string HotelLocation { get; set; } = "";
    public string RoomName { get; set; } = "";
    public string GuestName { get; set; } = "";
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public int GuestCount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "IDR";
    public BookingStatus Status { get; set; } = BookingStatus.PendingPayment;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
}

public class Payment
{
    public Guid PaymentId { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public string PaymentReference { get; set; } = "";
    public string? ExternalTransactionId { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "IDR";
    public string? Instructions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
}

public class Invoice
{
    public Guid InvoiceId { get; set; } = Guid.NewGuid();
    public string InvoiceNumber { get; set; } = "";
    public Guid BookingId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "IDR";
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
}

public class ETicket
{
    public Guid ETicketId { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public string QrCode { get; set; } = "";
    public string PdfReference { get; set; } = "";
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
}

public class Cancellation
{
    public Guid CancellationId { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public string? Reason { get; set; }
    public DateTime CancelledAt { get; set; } = DateTime.UtcNow;
    public string PolicyReference { get; set; } = "";
    public bool Refundable { get; set; }
    public decimal PenaltyRate { get; set; }
}

public class Refund
{
    public Guid RefundId { get; set; } = Guid.NewGuid();
    public Guid CancellationId { get; set; }
    public Guid BookingId { get; set; }
    public string PaymentReference { get; set; } = "";
    public decimal TotalPaid { get; set; }
    public decimal PenaltyAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public string Currency { get; set; } = "IDR";
    public RefundStatus Status { get; set; } = RefundStatus.PendingApproval;
    public string? FinanceDecision { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class NotificationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public NotificationType Type { get; set; }
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public NotificationStatus Status { get; set; } = NotificationStatus.Sent;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Entity { get; set; } = "";
    public Guid EntityId { get; set; }
    public string Action { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Actor { get; set; } = "system";
    public DateTime At { get; set; } = DateTime.UtcNow;
}
