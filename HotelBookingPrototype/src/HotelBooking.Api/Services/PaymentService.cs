using System.Security.Cryptography;
using System.Text.Json;
using HotelBooking.Api.Data;
using HotelBooking.Api.Dtos;
using HotelBooking.Api.Infrastructure;
using HotelBooking.Api.Models;

namespace HotelBooking.Api.Services;

public class PaymentService
{
    private readonly InMemoryDb _db;
    private readonly IntegrationSettings _integration;
    private readonly ETicketService _etickets;
    private readonly NotificationService _notifications;

    public PaymentService(InMemoryDb db, IntegrationSettings integration, ETicketService etickets, NotificationService notifications)
    {
        _db = db;
        _integration = integration;
        _etickets = etickets;
        _notifications = notifications;
    }

    public PaymentDto Create(Guid customerId, CreatePaymentRequest req)
    {
        var booking = _db.Bookings.GetValueOrDefault(req.BookingId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Booking tidak ditemukan.");
        if (booking.CustomerId != customerId)
            throw new BusinessException(403, "FORBIDDEN", "Booking bukan milik Anda.");
        if (booking.Status == BookingStatus.Expired || booking.ExpiresAt < DateTime.UtcNow)
            throw new BusinessException(422, "BOOKING_EXPIRED", "Booking melewati batas waktu pembayaran 30 menit.");
        if (booking.Status != BookingStatus.PendingPayment)
            throw new BusinessException(409, "PAYMENT_ALREADY_SETTLED", "Booking sudah tidak dalam status menunggu pembayaran.");

        var payment = new Payment
        {
            BookingId = booking.BookingId,
            PaymentReference = $"PAY-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
            Method = req.Method,
            Status = PaymentStatus.Pending,
            Amount = booking.TotalAmount,
            Currency = booking.Currency,
            Instructions = req.Method switch
            {
                PaymentMethod.VA => $"Virtual Account: 8808{RandomNumberGenerator.GetInt32(100000000, 999999999)} (contoh prototipe)",
                PaymentMethod.QRIS => "Scan QRIS pada aplikasi pembayaran Anda (contoh prototipe)",
                PaymentMethod.CC => "Masukkan detail kartu pada halaman payment gateway (contoh prototipe)",
                PaymentMethod.EWALLET => "Buka aplikasi e-wallet dan konfirmasi pembayaran (contoh prototipe)",
                _ => ""
            }
        };
        _db.Payments[payment.PaymentId] = payment;
        _db.AddAudit("Payment", payment.PaymentId, "PAYMENT_INITIATED", null, payment.PaymentReference, customerId.ToString());
        return ToDto(payment);
    }

    /// <summary>
    /// Callback dari Payment Gateway. Signature diverifikasi (HMAC-SHA256 raw body, header X-Signature),
    /// idempoten berdasarkan ExternalTransactionId (TSD 5.3).
    /// </summary>
    public object HandleCallback(string rawBody, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || !HmacHelper.Verify(rawBody, _integration.CallbackSecret, signature))
            throw new BusinessException(400, "INVALID_SIGNATURE", "Signature callback tidak valid.");

        var dto = JsonSerializer.Deserialize<PaymentCallbackDto>(rawBody,
                     new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                 ?? throw new BusinessException(400, "VALIDATION_ERROR", "Payload callback tidak valid.");

        var booking = _db.Bookings.Values.FirstOrDefault(b => b.BookingNumber == dto.OrderId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Booking tidak ditemukan.");
        var payment = _db.Payments.Values.FirstOrDefault(p => p.BookingId == booking.BookingId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Payment tidak ditemukan.");

        // Idempotensi: callback berulang dengan transaksi yang sama = no-op (UAT-PAY-06).
        if (payment.ExternalTransactionId == dto.ExternalTransactionId && payment.Status != PaymentStatus.Pending)
            return new { duplicate = true, message = "Callback sudah pernah diproses." };
        if (payment.Status != PaymentStatus.Pending)
            throw new BusinessException(409, "PAYMENT_ALREADY_SETTLED", "Pembayaran sudah berstatus final.");
        if (dto.Amount != payment.Amount || dto.Currency != payment.Currency)
            throw new BusinessException(409, "AMOUNT_MISMATCH", "Nominal pembayaran tidak sesuai.");

        // UAT-PAY-07: callback terlambat untuk booking Expired/Cancelled TIDAK mengubah status booking
        // dan tidak menerbitkan invoice/e-ticket; dicatat untuk rekonsiliasi manual.
        if (booking.Status == BookingStatus.Expired || booking.Status == BookingStatus.Cancelled)
        {
            _db.AddAudit("Payment", payment.PaymentId, "LATE_CALLBACK_RECONCILIATION",
                booking.Status.ToString(), dto.Status, "payment-gateway");
            return new
            {
                late_callback = true,
                message = "Booking sudah expired/cancelled. Callback dicatat untuk rekonsiliasi manual; status booking tidak berubah."
            };
        }

        payment.ExternalTransactionId = dto.ExternalTransactionId;

        switch (dto.Status.ToUpperInvariant())
        {
            case "SUCCESS":
                payment.Status = PaymentStatus.Success;
                payment.PaidAt = dto.PaidAt;
                booking.Status = BookingStatus.Paid;
                booking.PaidAt = dto.PaidAt;

                var invoice = new Invoice
                {
                    InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{RandomNumberGenerator.GetInt32(0, 0xFFFFFF):X6}",
                    BookingId = booking.BookingId,
                    PaymentId = payment.PaymentId,
                    TotalAmount = payment.Amount,
                    Currency = payment.Currency
                };
                _db.Invoices[invoice.InvoiceId] = invoice;

                var ticket = _etickets.Issue(booking);
                var customer = _db.Customers[booking.CustomerId];
                _notifications.SendETicket(customer.Email, booking.BookingNumber, ticket.QrCode);

                _db.AddAudit("Booking", booking.BookingId, "PAID", "PendingPayment", "Paid", "payment-gateway");
                return new { duplicate = false, booking_status = "PAID", invoice_number = invoice.InvoiceNumber };

            case "FAILED":
                payment.Status = PaymentStatus.Failed;
                _db.AddAudit("Payment", payment.PaymentId, "PAYMENT_FAILED", "Pending", "Failed", "payment-gateway");
                return new { duplicate = false, booking_status = booking.Status.ToString().ToUpperInvariant() };

            default:
                throw new BusinessException(400, "VALIDATION_ERROR", $"Status callback tidak dikenal: {dto.Status}");
        }
    }

    /// <summary>
    /// Endpoint bantuan prototipe: mensimulasikan callback bertanda tangan dari Payment Gateway.
    /// Parameter opsional: amount (simulasi nominal tidak cocok, UAT-PAY-05), txnId (simulasi callback
    /// berulang dengan transaksi yang sama, UAT-PAY-06).
    /// </summary>
    public object SimulateCallback(Guid paymentId, string status, decimal? amount = null, string? txnId = null,
        Guid requesterId = default, string role = "")
    {
        var payment = _db.Payments.GetValueOrDefault(paymentId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Payment tidak ditemukan.");
        var booking = _db.Bookings[payment.BookingId];
        // UAT-SEC-01: sesi simulasi pun wajib menghormati kepemilikan data antar Customer.
        if (role is not ("Admin" or "Finance") && booking.CustomerId != requesterId)
            throw new BusinessException(403, "FORBIDDEN", "Payment bukan milik Anda.");

        // Callback berulang untuk pembayaran yang sudah final = no-op, bukan error (UAT-PAY-06).
        if (payment.Status != PaymentStatus.Pending)
            return new { duplicate = true, message = "Callback sudah pernah diproses." };

        var dto = new PaymentCallbackDto(
            txnId ?? ($"TXN-{Guid.NewGuid():N}"[..16].ToUpperInvariant()),
            booking.BookingNumber, status, amount ?? payment.Amount, payment.Currency, DateTime.UtcNow);
        var raw = JsonSerializer.Serialize(dto);
        var signature = HmacHelper.Sign(raw, _integration.CallbackSecret);
        return HandleCallback(raw, signature);
    }

    public PaymentDto Get(Guid paymentId, Guid customerId)
    {
        var payment = _db.Payments.GetValueOrDefault(paymentId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Payment tidak ditemukan.");
        var booking = _db.Bookings[payment.BookingId];
        if (booking.CustomerId != customerId)
            throw new BusinessException(403, "FORBIDDEN", "Payment bukan milik Anda.");
        return ToDto(payment);
    }

    public static PaymentDto ToDto(Payment p) => new(
        p.PaymentId, p.BookingId, p.PaymentReference, p.Method,
        BookingService.EnumText(p.Status), p.Amount, p.Currency, p.Instructions, p.CreatedAt, p.PaidAt);
}

public class ETicketService
{
    private readonly InMemoryDb _db;
    private readonly IntegrationSettings _integration;

    public ETicketService(InMemoryDb db, IntegrationSettings integration)
    {
        _db = db;
        _integration = integration;
    }

    public ETicket Issue(Booking booking)
    {
        var existing = _db.ETickets.Values.FirstOrDefault(e => e.BookingId == booking.BookingId);
        if (existing != null) return existing;

        var token = HmacHelper.Sign(booking.BookingNumber, _integration.CallbackSecret)[..32];
        var ticket = new ETicket
        {
            BookingId = booking.BookingId,
            QrCode = $"{booking.BookingNumber}.{token}",
            PdfReference = $"eticket/{booking.BookingNumber}.pdf"
        };
        _db.ETickets[ticket.ETicketId] = ticket;
        return ticket;
    }

    public ETicketVerifyDto Verify(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || !code.Contains('.'))
            throw new BusinessException(400, "VALIDATION_ERROR", "Kode QR tidak valid.");
        var parts = code.Split('.', 2);
        var bookingNumber = parts[0];
        var token = parts[1];
        var expected = HmacHelper.Sign(bookingNumber, _integration.CallbackSecret)[..32];
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(token), System.Text.Encoding.UTF8.GetBytes(expected)))
            throw new BusinessException(404, "NOT_FOUND", "Kode QR tidak valid.");

        var booking = _db.Bookings.Values.FirstOrDefault(b => b.BookingNumber == bookingNumber)
            ?? throw new BusinessException(404, "NOT_FOUND", "Booking tidak ditemukan.");
        return new ETicketVerifyDto(booking.BookingNumber, booking.HotelName, booking.GuestName,
            booking.CheckIn, booking.CheckOut, booking.Status.ToString().ToUpperInvariant(),
            ValidForCheckIn: booking.Status == BookingStatus.Paid);
    }

    public ETicketDto Reissue(Guid bookingId, Guid customerId)
    {
        var booking = _db.Bookings.GetValueOrDefault(bookingId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Booking tidak ditemukan.");
        if (booking.CustomerId != customerId)
            throw new BusinessException(403, "FORBIDDEN", "Booking bukan milik Anda.");
        if (booking.Status != BookingStatus.Paid)
            throw new BusinessException(422, "VALIDATION_ERROR", "E-Ticket hanya untuk booking berstatus Paid.");
        var ticket = Issue(booking);
        return ToDto(ticket, booking);
    }

    /// <summary>DTO e-ticket memuat seluruh data yang harus tampil pada PDF (UAT-TKT-01).</summary>
    public static ETicketDto ToDto(ETicket ticket, Booking b) => new(
        ticket.ETicketId, ticket.BookingId, ticket.QrCode, ticket.PdfReference, ticket.IssuedAt,
        b.BookingNumber, b.HotelName, b.HotelLocation, b.RoomName, b.GuestName,
        b.CheckIn, b.CheckOut, b.TotalAmount, b.Currency);
}
