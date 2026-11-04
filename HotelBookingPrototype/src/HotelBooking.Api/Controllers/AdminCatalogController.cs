using HotelBooking.Api.Data;
using HotelBooking.Api.Dtos;
using HotelBooking.Api.Infrastructure;
using HotelBooking.Api.Models;
using HotelBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBooking.Api.Controllers;

/// <summary>
/// Panel admin (MOD-ADMIN): dashboard, katalog hotel &amp; kamar (add/edit/nonaktif),
/// rekap booking, dan pengelolaan pengguna. RBAC: baca = Admin/Finance/Management,
/// tulis katalog &amp; pengguna = Admin (TSD 7.3).
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Finance,Management")]
[Route("api/v1/admin")]
public class AdminCatalogController : ControllerBase
{
    private readonly InMemoryDb _db;

    public AdminCatalogController(InMemoryDb db)
    {
        _db = db;
    }

    private string Actor => User.CurrentUserId().ToString();

    // ================= Dashboard =================

    [HttpGet("dashboard")]
    public IActionResult Dashboard()
    {
        var bookings = _db.Bookings.Values.ToList();
        var paidPayments = _db.Payments.Values.Where(p => p.Status == PaymentStatus.Success).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var data = new
        {
            totals = new
            {
                hotels = _db.Hotels.Count,
                activeHotels = _db.Hotels.Values.Count(h => h.IsActive),
                rooms = _db.Rooms.Count,
                users = _db.Customers.Count,
                bookings = bookings.Count,
                revenue = paidPayments.Sum(p => p.Amount),
                currency = "IDR",
                upcomingCheckIns = bookings.Count(b => b.Status == BookingStatus.Paid && b.CheckIn >= today && b.CheckIn <= today.AddDays(7))
            },
            bookingsByStatus = bookings.GroupBy(b => BookingService.EnumText(b.Status))
                .Select(g => new { status = g.Key, count = g.Count(), amount = g.Sum(b => b.TotalAmount) })
                .OrderBy(x => x.status).ToList(),
            revenueByMethod = paidPayments.GroupBy(p => BookingService.EnumText(p.Method))
                .Select(g => new { method = g.Key, count = g.Count(), amount = g.Sum(p => p.Amount) })
                .OrderByDescending(x => x.amount).ToList(),
            pendingRefunds = new
            {
                count = _db.Refunds.Values.Count(r => r.Status == RefundStatus.PendingApproval),
                amount = _db.Refunds.Values.Where(r => r.Status == RefundStatus.PendingApproval).Sum(r => r.RefundAmount)
            },
            recentBookings = bookings.OrderByDescending(b => b.CreatedAt).Take(8)
                .Select(b => AdminBookingRow(b)).ToList()
        };
        return Ok(new { status = "SUCCESS", data });
    }

    // ================= Katalog Hotel =================

    [HttpGet("hotels")]
    public IActionResult ListHotels([FromQuery] string? q, [FromQuery] bool? isActive)
    {
        var query = _db.Hotels.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(h => h.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                     h.City.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                     h.HotelId.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (isActive.HasValue)
            query = query.Where(h => h.IsActive == isActive.Value);

        var items = query.OrderBy(h => h.Name).Select(h => new
        {
            h.HotelId, h.Name, h.City, h.Location, h.Rating, h.Facilities, h.IsActive,
            roomCount = _db.Rooms.Values.Count(r => r.HotelId == h.HotelId),
            totalRoomUnits = _db.Rooms.Values.Where(r => r.HotelId == h.HotelId).Sum(r => r.TotalRooms),
            bookingCount = _db.Bookings.Values.Count(b => b.HotelId == h.HotelId),
            rooms = _db.Rooms.Values.Where(r => r.HotelId == h.HotelId).Select(RoomRow).ToList()
        }).ToList();
        return Ok(new { status = "SUCCESS", data = new { items, total = items.Count } });
    }

    [HttpPost("hotels")]
    [Authorize(Roles = "Admin")]
    public IActionResult CreateHotel([FromBody] AdminHotelRequest req)
    {
        var hotel = new Hotel
        {
            HotelId = string.IsNullOrWhiteSpace(req.HotelId) ? GenerateHotelId(req.City) : req.HotelId.Trim(),
            Name = (req.Name ?? "").Trim(),
            City = (req.City ?? "").Trim(),
            Location = (req.Location ?? "").Trim(),
            Rating = req.Rating ?? 0,
            Facilities = (req.Facilities ?? new()).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList(),
            IsActive = true
        };
        ValidateHotel(hotel.Name, hotel.City, hotel.Location, hotel.Rating);
        if (_db.Hotels.ContainsKey(hotel.HotelId))
            throw new BusinessException(409, "DUPLICATE", $"Hotel ID {hotel.HotelId} sudah digunakan.");
        _db.Hotels[hotel.HotelId] = hotel;

        var rooms = new List<Room>();
        foreach (var r in req.Rooms ?? new())
        {
            var room = BuildRoom(hotel.HotelId, null, r);
            _db.Rooms[room.RoomId] = room;
            rooms.Add(room);
        }

        _db.AddAudit("Hotel", Guid.NewGuid(), "CREATED", null, hotel.HotelId, Actor);
        return Ok(new { status = "SUCCESS", data = HotelWithRooms(hotel, rooms) });
    }

    [HttpPut("hotels/{hotelId}")]
    [Authorize(Roles = "Admin")]
    public IActionResult UpdateHotel(string hotelId, [FromBody] AdminHotelRequest req)
    {
        var hotel = _db.Hotels.GetValueOrDefault(hotelId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Hotel tidak ditemukan.");
        ValidateHotel((req.Name ?? "").Trim(), (req.City ?? "").Trim(), (req.Location ?? "").Trim(), req.Rating ?? hotel.Rating);

        var old = $"{hotel.Name}|{hotel.City}|{hotel.Rating}|{hotel.IsActive}";
        hotel.Name = (req.Name ?? "").Trim();
        hotel.City = (req.City ?? "").Trim();
        hotel.Location = (req.Location ?? "").Trim();
        hotel.Rating = req.Rating ?? hotel.Rating;
        if (req.Facilities != null)
            hotel.Facilities = req.Facilities.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList();
        if (req.IsActive.HasValue) hotel.IsActive = req.IsActive.Value;

        _db.AddAudit("Hotel", Guid.NewGuid(), "UPDATED", old, $"{hotel.Name}|{hotel.City}|{hotel.Rating}|{hotel.IsActive}", Actor);
        return Ok(new { status = "SUCCESS", data = HotelWithRooms(hotel) });
    }

    [HttpPost("hotels/{hotelId}/status")]
    [Authorize(Roles = "Admin")]
    public IActionResult SetHotelStatus(string hotelId, [FromBody] AdminStatusRequest req)
    {
        var hotel = _db.Hotels.GetValueOrDefault(hotelId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Hotel tidak ditemukan.");
        var old = hotel.IsActive.ToString();
        hotel.IsActive = req.IsActive;
        _db.AddAudit("Hotel", Guid.NewGuid(), "STATUS_CHANGED", old, hotel.IsActive.ToString(), Actor);
        return Ok(new { status = "SUCCESS", data = HotelWithRooms(hotel) });
    }

    // ================= Katalog Kamar =================

    [HttpPost("hotels/{hotelId}/rooms")]
    [Authorize(Roles = "Admin")]
    public IActionResult CreateRoom(string hotelId, [FromBody] AdminRoomRequest req)
    {
        var hotel = _db.Hotels.GetValueOrDefault(hotelId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Hotel tidak ditemukan.");
        var room = BuildRoom(hotel.HotelId, null, req);
        _db.Rooms[room.RoomId] = room;
        _db.AddAudit("Room", Guid.NewGuid(), "CREATED", null, room.RoomId, Actor);
        return Ok(new { status = "SUCCESS", data = RoomRow(room) });
    }

    [HttpPut("rooms/{roomId}")]
    [Authorize(Roles = "Admin")]
    public IActionResult UpdateRoom(string roomId, [FromBody] AdminRoomRequest req)
    {
        var room = _db.Rooms.GetValueOrDefault(roomId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Tipe kamar tidak ditemukan.");
        var old = $"{room.Name}|{room.PricePerNight}|{room.TotalRooms}|{room.Capacity}";
        ValidateRoom(req.Name, req.Capacity, req.PricePerNight, req.TotalRooms, req.Policy);
        room.Name = (req.Name ?? "").Trim();
        room.Capacity = req.Capacity;
        room.PricePerNight = req.PricePerNight;
        room.Currency = string.IsNullOrWhiteSpace(req.Currency) ? "IDR" : req.Currency.Trim();
        room.TotalRooms = req.TotalRooms;
        room.Policy = req.Policy ?? "Flexible";
        room.PenaltyRate = req.PenaltyRate ?? (room.Policy == "NonRefundable" ? 1.00m : 0.10m);
        _db.AddAudit("Room", Guid.NewGuid(), "UPDATED", old, $"{room.Name}|{room.PricePerNight}|{room.TotalRooms}|{room.Capacity}", Actor);
        return Ok(new { status = "SUCCESS", data = RoomRow(room) });
    }

    [HttpDelete("rooms/{roomId}")]
    [Authorize(Roles = "Admin")]
    public IActionResult DeleteRoom(string roomId)
    {
        var room = _db.Rooms.GetValueOrDefault(roomId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Tipe kamar tidak ditemukan.");
        if (_db.Bookings.Values.Any(b => b.RoomId == roomId))
            throw new BusinessException(409, "IN_USE", "Kamar memiliki riwayat booking dan tidak dapat dihapus. Nonaktifkan hotel atau ubah stok menjadi 0.");
        _db.Rooms.TryRemove(roomId, out _);
        _db.AddAudit("Room", Guid.NewGuid(), "DELETED", roomId, null, Actor);
        return Ok(new { status = "SUCCESS", data = new { roomId, deleted = true } });
    }

    // ================= Rekap Booking =================

    /// <summary>Rekap booking seluruh pelanggan: ringkasan per status, per hotel, dan tren harian.</summary>
    [HttpGet("bookings/recap")]
    public IActionResult BookingRecap([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var query = _db.Bookings.Values.AsEnumerable();
        if (from.HasValue) query = query.Where(b => b.CheckIn >= from.Value);
        if (to.HasValue) query = query.Where(b => b.CheckIn <= to.Value);
        var bookings = query.ToList();

        var paid = bookings.Where(b => b.Status == BookingStatus.Paid).ToList();
        var data = new
        {
            totals = new
            {
                bookings = bookings.Count,
                paid = paid.Count,
                revenue = paid.Sum(b => b.TotalAmount),
                averageBookingValue = paid.Count == 0 ? 0 : Math.Round(paid.Average(b => b.TotalAmount), 2),
                roomNights = paid.Sum(b => b.CheckOut.DayNumber - b.CheckIn.DayNumber),
                cancelled = bookings.Count(b => b.Status == BookingStatus.Cancelled),
                currency = "IDR"
            },
            byStatus = bookings.GroupBy(b => BookingService.EnumText(b.Status))
                .Select(g => new { status = g.Key, count = g.Count(), amount = g.Sum(b => b.TotalAmount) })
                .OrderBy(x => x.status).ToList(),
            byHotel = bookings.GroupBy(b => new { b.HotelId, b.HotelName })
                .Select(g => new
                {
                    hotelId = g.Key.HotelId, hotelName = g.Key.HotelName,
                    bookings = g.Count(),
                    paid = g.Count(b => b.Status == BookingStatus.Paid),
                    revenue = g.Where(b => b.Status == BookingStatus.Paid).Sum(b => b.TotalAmount)
                }).OrderByDescending(x => x.revenue).ToList(),
            daily = bookings.Where(b => b.Status == BookingStatus.Paid)
                .GroupBy(b => b.CreatedAt.Date)
                .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), count = g.Count(), revenue = g.Sum(b => b.TotalAmount) })
                .OrderBy(x => x.date).ToList()
        };
        return Ok(new { status = "SUCCESS", data });
    }

    /// <summary>Daftar booking seluruh pelanggan (recap detail) dengan filter &amp; paginasi.</summary>
    [HttpGet("bookings")]
    public IActionResult ListBookings([FromQuery] string? status, [FromQuery] string? hotelId,
        [FromQuery] string? q, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        if (page < 1) page = 1;
        if (size is < 1 or > 200) size = 20;

        var query = _db.Bookings.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(b => BookingService.EnumText(b.Status).Equals(status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(hotelId))
            query = query.Where(b => b.HotelId == hotelId);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(b =>
                b.BookingNumber.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.GuestName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.HotelName.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (from.HasValue) query = query.Where(b => b.CheckIn >= from.Value);
        if (to.HasValue) query = query.Where(b => b.CheckIn <= to.Value);

        var total = query.Count();
        var items = query.OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(AdminBookingRow).ToList();
        return Ok(new { status = "SUCCESS", data = new { items, total, page, size } });
    }

    // ================= Pengguna =================

    [HttpGet("users")]
    [Authorize(Roles = "Admin")]
    public IActionResult ListUsers([FromQuery] string? q)
    {
        var query = _db.Customers.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.Email.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                     (u.FullName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
        var items = query.OrderBy(u => u.Email).Select(u => new
        {
            u.CustomerId, u.Email, u.FullName, u.PhoneNumber, u.Role,
            status = BookingService.EnumText(u.AccountStatus),
            u.FailedAttemptCount, u.LastLoginAt, u.CreatedAt
        }).ToList();
        return Ok(new { status = "SUCCESS", data = new { items, total = items.Count } });
    }

    [HttpPost("users/{customerId:guid}/status")]
    [Authorize(Roles = "Admin")]
    public IActionResult SetUserStatus(Guid customerId, [FromBody] AdminUserStatusRequest req)
    {
        var user = _db.Customers.GetValueOrDefault(customerId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Pengguna tidak ditemukan.");
        if (!Enum.TryParse<AccountStatus>(req.Status, true, out var newStatus))
            throw new BusinessException(400, "VALIDATION_ERROR", "Status harus salah satu dari: Active, Blocked, Locked, PendingVerification.");
        if (user.Role == "Admin" && newStatus != AccountStatus.Active)
            throw new BusinessException(409, "FORBIDDEN", "Akun admin tidak dapat dinonaktifkan.");
        var old = user.AccountStatus.ToString();
        user.AccountStatus = newStatus;
        if (newStatus == AccountStatus.Active) { user.FailedAttemptCount = 0; user.LockedUntil = null; }
        _db.AddAudit("Customer", customerId, "STATUS_CHANGED", old, newStatus.ToString(), Actor);
        return Ok(new { status = "SUCCESS", data = new { customerId, status = BookingService.EnumText(newStatus) } });
    }

    // ================= Helpers =================

    private object AdminBookingRow(Booking b)
    {
        var customer = _db.Customers.GetValueOrDefault(b.CustomerId);
        return new
        {
            b.BookingId, b.BookingNumber, b.HotelId, b.HotelName, b.RoomName, b.GuestName,
            customerName = customer?.FullName ?? customer?.Email ?? "-",
            customerEmail = customer?.Email ?? "-",
            b.CheckIn, b.CheckOut, b.GuestCount, b.TotalAmount, b.Currency,
            status = BookingService.EnumText(b.Status), b.CreatedAt, b.PaidAt, b.CancelledAt, b.ExpiredAt
        };
    }

    private static object RoomRow(Room r) => new
    {
        r.RoomId, r.HotelId, r.Name, r.Capacity, r.PricePerNight, r.Currency,
        r.TotalRooms, r.Policy, r.PenaltyRate
    };

    private static object HotelWithRooms(Hotel h, List<Room>? rooms = null) => new
    {
        h.HotelId, h.Name, h.City, h.Location, h.Rating, h.Facilities, h.IsActive,
        rooms = (rooms ?? new()).Select(RoomRow).ToList()
    };

    private Room BuildRoom(string hotelId, string? roomId, AdminRoomRequest req)
    {
        ValidateRoom(req.Name, req.Capacity, req.PricePerNight, req.TotalRooms, req.Policy);
        return new Room
        {
            RoomId = string.IsNullOrWhiteSpace(roomId)
                ? (string.IsNullOrWhiteSpace(req.RoomId) ? GenerateRoomId(hotelId) : req.RoomId.Trim())
                : roomId,
            HotelId = hotelId,
            Name = (req.Name ?? "").Trim(),
            Capacity = req.Capacity,
            PricePerNight = req.PricePerNight,
            Currency = string.IsNullOrWhiteSpace(req.Currency) ? "IDR" : req.Currency.Trim(),
            TotalRooms = req.TotalRooms,
            Policy = req.Policy ?? "Flexible",
            PenaltyRate = req.PenaltyRate ?? (req.Policy == "NonRefundable" ? 1.00m : 0.10m)
        };
    }

    private static void ValidateHotel(string name, string city, string location, double rating)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessException(400, "VALIDATION_ERROR", "Nama hotel wajib diisi.");
        if (string.IsNullOrWhiteSpace(city))
            throw new BusinessException(400, "VALIDATION_ERROR", "Kota wajib diisi.");
        if (string.IsNullOrWhiteSpace(location))
            throw new BusinessException(400, "VALIDATION_ERROR", "Alamat/lokasi wajib diisi.");
        if (rating is < 0 or > 5)
            throw new BusinessException(400, "VALIDATION_ERROR", "Rating harus antara 0 sampai 5.");
    }

    private static void ValidateRoom(string? name, int capacity, decimal pricePerNight, int totalRooms, string? policy)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessException(400, "VALIDATION_ERROR", "Nama tipe kamar wajib diisi.");
        if (capacity <= 0)
            throw new BusinessException(400, "VALIDATION_ERROR", "Kapasitas kamar harus angka positif.");
        if (pricePerNight <= 0)
            throw new BusinessException(400, "VALIDATION_ERROR", "Harga per malam harus angka positif.");
        if (totalRooms < 0)
            throw new BusinessException(400, "VALIDATION_ERROR", "Jumlah kamar tidak boleh negatif.");
        if (policy != null && policy is not ("Flexible" or "NonRefundable"))
            throw new BusinessException(400, "VALIDATION_ERROR", "Policy harus Flexible atau NonRefundable.");
    }

    private string GenerateHotelId(string? city)
    {
        var prefix = new string((city ?? "GEN").Where(char.IsLetter).Take(3).ToArray()).ToUpperInvariant();
        if (prefix.Length < 3) prefix = (prefix + "GEN").Substring(0, 3);
        string id;
        do { id = $"HTL-{prefix}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}"; }
        while (_db.Hotels.ContainsKey(id));
        return id;
    }

    private string GenerateRoomId(string hotelId)
    {
        string id;
        do { id = $"RM-{hotelId.Replace("HTL-", "")}-{Guid.NewGuid().ToString("N")[..3].ToUpperInvariant()}"; }
        while (_db.Rooms.ContainsKey(id));
        return id;
    }
}

// ---------- DTO request panel admin ----------

public record AdminHotelRequest(string? HotelId, string? Name, string? City, string? Location,
    double? Rating, List<string>? Facilities, bool? IsActive, List<AdminRoomRequest>? Rooms);

public record AdminRoomRequest(string? RoomId, string? Name, int Capacity, decimal PricePerNight,
    string? Currency, int TotalRooms, string? Policy, decimal? PenaltyRate);

public record AdminStatusRequest(bool IsActive);

public record AdminUserStatusRequest(string Status);
