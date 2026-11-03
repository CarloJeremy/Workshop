using HotelBooking.Api.Models;

namespace HotelBooking.Api.Dtos;

// ---------- Auth ----------
public record RegisterRequest(string Email, string Password, string? FullName, string? PhoneNumber);
public record OtpRequest(string Email, string OtpCode);
public record ResendOtpRequest(string Email);
public record LoginRequest(string EmailOrPhone, string Password);
public record RefreshRequest(string RefreshToken);
public record LogoutRequest(string? RefreshToken);

// ---------- Hotel ----------
public record HotelSearchResult(string HotelId, string Name, string City, string Location, double Rating,
    List<string> Facilities, List<RoomAvailabilityDto> Rooms);
public record RoomAvailabilityDto(string RoomId, string Name, int Capacity, decimal PricePerNight, string Currency,
    int AvailableRooms, string Policy);

// ---------- Booking ----------
public record CreateBookingRequest(string HotelId, string RoomId, string GuestName, DateOnly CheckIn, DateOnly CheckOut, int GuestCount,
    decimal? ExpectedPricePerNight = null);
public record BookingDto(Guid BookingId, string BookingNumber, string HotelName, string HotelLocation, string RoomName, string GuestName,
    DateOnly CheckIn, DateOnly CheckOut, int GuestCount, decimal TotalAmount, string Currency,
    string Status, DateTime ExpiresAt, DateTime CreatedAt, DateTime? PaidAt, DateTime? CancelledAt, DateTime? ExpiredAt);
public record CancellationPreviewDto(string Policy, bool Refundable, decimal TotalPaid, decimal PenaltyRate,
    decimal EstimatedPenalty, decimal EstimatedRefund, string Notes);

// ---------- Payment ----------
public record CreatePaymentRequest(Guid BookingId, PaymentMethod Method);
public record PaymentDto(Guid PaymentId, Guid BookingId, string PaymentReference, PaymentMethod Method,
    string Status, decimal Amount, string Currency, string? Instructions, DateTime CreatedAt, DateTime? PaidAt);
public record PaymentCallbackDto(string ExternalTransactionId, string OrderId, string Status, decimal Amount,
    string Currency, DateTime PaidAt);
public record InvoiceDto(string InvoiceNumber, Guid BookingId, string BookingNumber, string CustomerName, string CustomerEmail,
    Guid PaymentId, string PaymentMethod, string PaymentStatus, decimal TotalAmount, string Currency, DateTime IssuedAt);

// ---------- E-Ticket ----------
public record ETicketDto(Guid ETicketId, Guid BookingId, string QrCode, string PdfReference, DateTime IssuedAt,
    string BookingNumber, string HotelName, string HotelLocation, string RoomName, string GuestName,
    DateOnly CheckIn, DateOnly CheckOut, decimal TotalAmount, string Currency);
public record ETicketVerifyDto(string BookingNumber, string HotelName, string GuestName, DateOnly CheckIn, DateOnly CheckOut,
    string BookingStatus, bool ValidForCheckIn);

// ---------- Refund ----------
public record RefundDto(Guid RefundId, Guid CancellationId, Guid BookingId, string PaymentReference,
    decimal TotalPaid, decimal PenaltyAmount, decimal RefundAmount, string Currency, string Status,
    string? FinanceDecision, string? Reason, DateTime CreatedAt, DateTime? ProcessedAt, DateTime? CompletedAt);
public record RejectRefundRequest(string Reason);

// ---------- Common ----------
public record PageResult<T>(List<T> Items, int Total, int Page, int Size);
