using HotelBooking.Api.Data;
using HotelBooking.Api.Dtos;
using HotelBooking.Api.Infrastructure;
using HotelBooking.Api.Models;

namespace HotelBooking.Api.Services;

public class CancellationService
{
    private readonly InMemoryDb _db;

    public CancellationService(InMemoryDb db) => _db = db;

    public CancellationPreviewDto Preview(Guid bookingId, Guid customerId)
    {
        var booking = GetOwned(bookingId, customerId);
        var room = _db.Rooms[booking.RoomId];
        var (canCancel, policyRefundable, penaltyRate, notes) = EvaluatePolicy(booking, room);

        // Refund hanya untuk pembayaran yang berhasil (TSD 4.9 aturan 1).
        var isPaid = booking.Status == BookingStatus.Paid;
        if (!isPaid)
            notes = "Booking belum dibayar: mekanisme pembatalan booking berbayar tidak dapat digunakan." + (canCancel ? "" : " " + notes);
        else if (!canCancel)
            notes = notes;

        var refundable = canCancel && policyRefundable && isPaid;
        var penalty = refundable ? booking.TotalAmount * penaltyRate : booking.TotalAmount;
        var estimatedRefund = refundable ? booking.TotalAmount - penalty : 0m;

        return new CancellationPreviewDto(room.Policy, refundable, booking.TotalAmount, penaltyRate,
            penalty, estimatedRefund, notes);
    }

    public object Cancel(Guid bookingId, Guid customerId, string? reason)
    {
        var booking = GetOwned(bookingId, customerId);
        if (_db.Cancellations.Values.Any(c => c.BookingId == bookingId))
            throw new BusinessException(409, "VALIDATION_ERROR", "Booking sudah pernah dibatalkan.");
        // UAT-CXL-05: mekanisme pembatalan booking berbayar hanya untuk booking yang telah dibayar.
        if (booking.Status != BookingStatus.Paid)
            throw new BusinessException(409, "VALIDATION_ERROR",
                "Pembatalan hanya dapat diproses untuk booking yang telah dibayar. Booking Pending Payment/Expired/Cancelled tidak dapat diproses mekanisme ini.");

        var room = _db.Rooms[booking.RoomId];
        var (canCancel, refundable, penaltyRate, notes) = EvaluatePolicy(booking, room);
        // UAT-CXL-04: tolak pembatalan yang melewati batas waktu sesuai policy.
        if (!canCancel)
            throw new BusinessException(409, "CANCELLATION_NOT_ALLOWED", notes);

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;

        var cancellation = new Cancellation
        {
            BookingId = booking.BookingId,
            Reason = reason,
            PolicyReference = room.Policy,
            Refundable = refundable,
            PenaltyRate = penaltyRate
        };
        _db.Cancellations[cancellation.CancellationId] = cancellation;

        Guid? refundId = null;
        if (cancellation.Refundable)
        {
            var payment = _db.Payments.Values.FirstOrDefault(p => p.BookingId == booking.BookingId && p.Status == PaymentStatus.Success);
            var totalPaid = payment?.Amount ?? booking.TotalAmount;
            var penalty = totalPaid * penaltyRate;
            var refund = new Refund
            {
                CancellationId = cancellation.CancellationId,
                BookingId = booking.BookingId,
                PaymentReference = payment?.PaymentReference ?? "",
                TotalPaid = totalPaid,
                PenaltyAmount = penalty,
                RefundAmount = totalPaid - penalty,
                Currency = booking.Currency,
                Status = RefundStatus.PendingApproval
            };
            _db.Refunds[refund.RefundId] = refund;
            refundId = refund.RefundId;
        }

        _db.AddAudit("Booking", booking.BookingId, "CANCELLED", "Paid", "Cancelled", customerId.ToString());
        return new
        {
            cancellation_id = cancellation.CancellationId,
            booking_status = "CANCELLED",
            refundable = cancellation.Refundable,
            refund_id = refundId
        };
    }

    /// <summary>
    /// Evaluasi policy pembatalan: (canCancel, refundable, penaltyRate, notes).
    /// - NonRefundable: pembatalan boleh diproses, tidak ada refund.
    /// - Flexible: refundable bila pembatalan >= 1 hari sebelum check-in; melewati batas waktu -> pembatalan ditolak (UAT-CXL-04).
    /// </summary>
    private static (bool canCancel, bool refundable, decimal penaltyRate, string notes) EvaluatePolicy(Booking booking, Room room)
    {
        if (room.Policy == "NonRefundable")
            return (true, false, 1.00m, "Kebijakan Non-Refundable: pembatalan tidak menghasilkan refund.");

        var minCancelDate = booking.CheckIn.AddDays(-1);
        if (DateOnly.FromDateTime(DateTime.UtcNow) > minCancelDate)
            return (false, false, 1.00m,
                "Melewati batas waktu pembatalan (maksimal 1 hari sebelum check-in): pembatalan tidak dapat diproses sesuai policy hotel.");

        return (true, true, room.PenaltyRate, "Kebijakan Flexible: refund dikurangi penalty sesuai policy hotel.");
    }

    private Booking GetOwned(Guid bookingId, Guid customerId)
    {
        var booking = _db.Bookings.GetValueOrDefault(bookingId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Booking tidak ditemukan.");
        if (booking.CustomerId != customerId)
            throw new BusinessException(403, "FORBIDDEN", "Booking bukan milik Anda.");
        return booking;
    }
}

public class RefundService
{
    private readonly InMemoryDb _db;

    public RefundService(InMemoryDb db) => _db = db;

    public RefundDto Get(Guid refundId, Guid requesterId, string role)
    {
        var refund = _db.Refunds.GetValueOrDefault(refundId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Refund tidak ditemukan.");
        if (role == "Customer")
        {
            var booking = _db.Bookings[refund.BookingId];
            if (booking.CustomerId != requesterId)
                throw new BusinessException(403, "FORBIDDEN", "Refund bukan milik Anda.");
        }
        return ToDto(refund);
    }

    public List<RefundDto> List(Guid requesterId, string role)
    {
        var query = _db.Refunds.Values.AsEnumerable();
        if (role == "Customer")
            query = query.Where(r => _db.Bookings[r.BookingId].CustomerId == requesterId);
        return query.OrderByDescending(r => r.CreatedAt).Select(ToDto).ToList();
    }

    public RefundDto Approve(Guid refundId, string actor)
    {
        var refund = _db.Refunds.GetValueOrDefault(refundId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Refund tidak ditemukan.");
        if (refund.Status != RefundStatus.PendingApproval)
            throw new BusinessException(409, "VALIDATION_ERROR", "Refund tidak dalam status menunggu persetujuan.");

        refund.Status = RefundStatus.Approved;
        refund.FinanceDecision = "APPROVED";
        _db.AddAudit("Refund", refund.RefundId, "APPROVED", "PendingApproval", "Approved", actor);
        return ToDto(refund);
    }

    public RefundDto Reject(Guid refundId, string reason, string actor)
    {
        var refund = _db.Refunds.GetValueOrDefault(refundId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Refund tidak ditemukan.");
        if (refund.Status != RefundStatus.PendingApproval)
            throw new BusinessException(409, "VALIDATION_ERROR", "Refund tidak dalam status menunggu persetujuan.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessException(400, "VALIDATION_ERROR", "Alasan penolakan wajib diisi.");

        refund.Status = RefundStatus.Rejected;
        refund.FinanceDecision = "REJECTED";
        refund.Reason = reason;
        _db.AddAudit("Refund", refund.RefundId, "REJECTED", "PendingApproval", "Rejected", actor);
        return ToDto(refund);
    }

    /// <summary>
    /// Eksekusi refund via Payment Gateway. Prototipe: gateway mock langsung berhasil.
    /// simulate=FAILED mensimulasikan provider gagal memproses refund (UAT-RFD-07) → status Failed.
    /// </summary>
    public RefundDto Execute(Guid refundId, string actor, string? simulate = null)
    {
        var refund = _db.Refunds.GetValueOrDefault(refundId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Refund tidak ditemukan.");
        if (refund.Status != RefundStatus.Approved)
            throw new BusinessException(409, "VALIDATION_ERROR", "Hanya refund berstatus Approved yang dapat dieksekusi.");

        refund.Status = RefundStatus.Processing;
        refund.ProcessedAt = DateTime.UtcNow;

        if (string.Equals(simulate, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            // UAT-RFD-07: kegagalan provider tercatat (bukan Completed) untuk tindak lanjut/pengulangan.
            refund.Status = RefundStatus.Failed;
            refund.Reason = "Provider refund gagal (simulasi). Detail kegagalan tercatat untuk tindak lanjut/pengulangan.";
            _db.AddAudit("Refund", refund.RefundId, "FAILED", "Processing", "Failed", actor);
            return ToDto(refund);
        }

        // Mock gateway: langsung Completed. Implementasi nyata: panggil API refund provider + polling (job TSD 8).
        refund.Status = RefundStatus.Completed;
        refund.CompletedAt = DateTime.UtcNow;
        _db.AddAudit("Refund", refund.RefundId, "COMPLETED", "Processing", "Completed", actor);
        return ToDto(refund);
    }

    public static RefundDto ToDto(Refund r) => new(
        r.RefundId, r.CancellationId, r.BookingId, r.PaymentReference,
        r.TotalPaid, r.PenaltyAmount, r.RefundAmount, r.Currency,
        BookingService.EnumText(r.Status), r.FinanceDecision, r.Reason,
        r.CreatedAt, r.ProcessedAt, r.CompletedAt);
}

/// <summary>
/// Job background: menandai booking PendingPayment yang melewati batas 30 menit menjadi Expired
/// dan melepaskan hold inventory (TSD bagian 8 - Booking Expiry).
/// </summary>
public class BookingExpiryService : BackgroundService
{
    private readonly Data.InMemoryDb _db;
    private readonly BookingSettings _settings;

    public BookingExpiryService(Data.InMemoryDb db, BookingSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.ExpiryScanSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            foreach (var booking in _db.Bookings.Values.Where(b =>
                         b.Status == BookingStatus.PendingPayment && b.ExpiresAt <= now))
            {
                booking.Status = BookingStatus.Expired;
                booking.ExpiredAt = now;
                _db.AddAudit("Booking", booking.BookingId, "EXPIRED", "PendingPayment", "Expired", "system-expiry-job");
            }
        }
    }
}
