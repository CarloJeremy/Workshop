using HotelBooking.Api.Dtos;
using HotelBooking.Api.Infrastructure;
using HotelBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBooking.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/bookings")]
public class BookingsController : ControllerBase
{
    private readonly BookingService _bookings;
    private readonly CancellationService _cancellations;
    private readonly Data.InMemoryDb _db;

    public BookingsController(BookingService bookings, CancellationService cancellations, Data.InMemoryDb db)
    {
        _bookings = bookings;
        _cancellations = cancellations;
        _db = db;
    }

    [HttpPost]
    [Authorize(Roles = "Customer,Admin,TravelConsultant")]
    public IActionResult Create([FromBody] CreateBookingRequest req)
        => Ok(new { status = "SUCCESS", data = _bookings.Create(User.CurrentUserId(), req) });

    [HttpGet("history")]
    public IActionResult History([FromQuery] string group = "inprogress", [FromQuery] int page = 1, [FromQuery] int size = 10)
        => Ok(new { status = "SUCCESS", data = _bookings.History(User.CurrentUserId(), group, page, size) });

    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id)
        => Ok(new { status = "SUCCESS", data = _bookings.Get(id, User.CurrentUserId()) });

    [HttpGet("{id:guid}/cancellation-preview")]
    public IActionResult CancellationPreview(Guid id)
        => Ok(new { status = "SUCCESS", data = _cancellations.Preview(id, User.CurrentUserId()) });

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Customer,Admin,TravelConsultant")]
    public IActionResult Cancel(Guid id, [FromBody] CancelRequest? req)
        => Ok(new { status = "SUCCESS", data = _cancellations.Cancel(id, User.CurrentUserId(), req?.Reason) });

    [HttpGet("{id:guid}/invoice")]
    public IActionResult Invoice(Guid id)
    {
        var booking = _bookings.Get(id, User.CurrentUserId());
        var invoice = _db.Invoices.Values.FirstOrDefault(i => i.BookingId == booking.BookingId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Invoice belum tersedia (pembayaran belum berhasil).");
        var entity = _db.Bookings[invoice.BookingId];
        var customer = _db.Customers[entity.CustomerId];
        var payment = _db.Payments[invoice.PaymentId];
        return Ok(new
        {
            status = "SUCCESS",
            data = new InvoiceDto(invoice.InvoiceNumber, invoice.BookingId, entity.BookingNumber,
                customer.FullName ?? customer.Email, customer.Email,
                invoice.PaymentId, BookingService.EnumText(payment.Method), BookingService.EnumText(payment.Status),
                invoice.TotalAmount, invoice.Currency, invoice.IssuedAt)
        });
    }
}

public record CancelRequest(string? Reason);

[ApiController]
[Authorize]
[Route("api/v1/payments")]
public class PaymentsController : ControllerBase
{
    private readonly PaymentService _payments;
    public PaymentsController(PaymentService payments) => _payments = payments;

    [HttpPost]
    [Authorize(Roles = "Customer,Admin,TravelConsultant")]
    public IActionResult Create([FromBody] CreatePaymentRequest req)
        => Ok(new { status = "SUCCESS", data = _payments.Create(User.CurrentUserId(), req) });

    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id)
        => Ok(new { status = "SUCCESS", data = _payments.Get(id, User.CurrentUserId()) });

    /// <summary>Callback dari Payment Gateway. Wajib menyertakan header X-Signature (HMAC-SHA256 raw body).</summary>
    [HttpPost("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback()
    {
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync();
        var signature = Request.Headers["X-Signature"].ToString();
        return Ok(new { status = "SUCCESS", data = _payments.HandleCallback(raw, signature) });
    }

    /// <summary>
    /// Bantuan prototipe: simulasi callback bertanda tangan dari gateway.
    /// Opsional: amount (UAT-PAY-05 nominal tidak cocok), txnId (UAT-PAY-06 callback berulang).
    /// </summary>
    [HttpPost("{id:guid}/simulate-callback")]
    public IActionResult Simulate(Guid id, [FromQuery] string status = "SUCCESS",
        [FromQuery] decimal? amount = null, [FromQuery] string? txnId = null)
        => Ok(new
        {
            status = "SUCCESS",
            data = _payments.SimulateCallback(id, status, amount, txnId,
                User.CurrentUserId(), User.FindFirst("role")?.Value ?? "")
        });
}

[ApiController]
[Route("api/v1/etickets")]
public class ETicketsController : ControllerBase
{
    private readonly ETicketService _etickets;
    public ETicketsController(ETicketService etickets) => _etickets = etickets;

    [HttpGet("verify")]
    [AllowAnonymous]
    public IActionResult Verify([FromQuery] string code)
        => Ok(new { status = "SUCCESS", data = _etickets.Verify(code) });

    [HttpPost("reissue")]
    [Authorize]
    public IActionResult Reissue([FromBody] ReissueRequest req)
        => Ok(new { status = "SUCCESS", data = _etickets.Reissue(req.BookingId, User.CurrentUserId()) });
}

public record ReissueRequest(Guid BookingId);

[ApiController]
[Authorize]
[Route("api/v1/refunds")]
public class RefundsController : ControllerBase
{
    private readonly RefundService _refunds;
    public RefundsController(RefundService refunds) => _refunds = refunds;

    [HttpGet]
    public IActionResult List()
        => Ok(new { status = "SUCCESS", data = _refunds.List(User.CurrentUserId(), User.FindFirst("role")?.Value ?? "") });

    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id)
        => Ok(new { status = "SUCCESS", data = _refunds.Get(id, User.CurrentUserId(), User.FindFirst("role")?.Value ?? "") });

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Finance,Admin")]
    public IActionResult Approve(Guid id)
        => Ok(new { status = "SUCCESS", data = _refunds.Approve(id, User.CurrentUserId().ToString()) });

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Finance,Admin")]
    public IActionResult Reject(Guid id, [FromBody] RejectRefundRequest req)
        => Ok(new { status = "SUCCESS", data = _refunds.Reject(id, req.Reason, User.CurrentUserId().ToString()) });

    [HttpPost("{id:guid}/execute")]
    [Authorize(Roles = "Finance,Admin")]
    public IActionResult Execute(Guid id, [FromQuery] string? simulate = null)
        => Ok(new { status = "SUCCESS", data = _refunds.Execute(id, User.CurrentUserId().ToString(), simulate) });
}

/// <summary>
/// Panel operasional (monitoring): jejak audit dan log notifikasi/email (UAT-AUD-01, UAT-TKT-05).
/// RBAC: Admin, Finance, Management (monitoring) — Customer tidak berwenang.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Finance,Management")]
[Route("api/v1/admin")]
public class AdminController : ControllerBase
{
    private readonly Data.InMemoryDb _db;
    public AdminController(Data.InMemoryDb db) => _db = db;

    [HttpGet("audits")]
    public IActionResult Audits([FromQuery] int page = 1, [FromQuery] int size = 50)
    {
        var q = _db.AuditLogs.Values.OrderByDescending(a => a.At).AsEnumerable();
        var items = q.Skip((page - 1) * size).Take(size).Select(a => new
        {
            a.Id, a.Entity, a.EntityId, a.Action, a.OldValue, a.NewValue, a.Actor, a.At
        }).ToList();
        return Ok(new { status = "SUCCESS", data = new { items, total = _db.AuditLogs.Count, page, size } });
    }

    [HttpGet("notifications")]
    public IActionResult Notifications([FromQuery] int page = 1, [FromQuery] int size = 50)
    {
        var q = _db.Notifications.Values.OrderByDescending(n => n.CreatedAt).AsEnumerable();
        var items = q.Skip((page - 1) * size).Take(size).Select(n => new
        {
            n.Id, Type = n.Type.ToString(), n.Recipient, n.Subject,
            Status = n.Status.ToString(), n.RetryCount, n.LastError, n.CreatedAt
        }).ToList();
        return Ok(new { status = "SUCCESS", data = new { items, total = _db.Notifications.Count, page, size } });
    }
}
