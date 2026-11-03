using System.Collections.Concurrent;
using System.Security.Cryptography;
using HotelBooking.Api.Data;
using HotelBooking.Api.Dtos;
using HotelBooking.Api.Infrastructure;
using HotelBooking.Api.Models;

namespace HotelBooking.Api.Services;

public class BookingService
{
    private readonly InMemoryDb _db;
    private readonly HotelService _hotels;
    private readonly BookingSettings _settings;
    private readonly ConcurrentDictionary<string, object> _locks = new();

    public BookingService(InMemoryDb db, HotelService hotels, BookingSettings settings)
    {
        _db = db;
        _hotels = hotels;
        _settings = settings;
    }

    public BookingDto Create(Guid customerId, CreateBookingRequest req)
    {
        var hotel = _db.Hotels.GetValueOrDefault(req.HotelId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Hotel tidak ditemukan.");
        var room = _db.Rooms.GetValueOrDefault(req.RoomId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Tipe kamar tidak ditemukan.");
        if (room.HotelId != hotel.HotelId)
            throw new BusinessException(400, "VALIDATION_ERROR", "Kamar tidak termasuk dalam hotel tersebut.");
        if (req.CheckIn < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new BusinessException(400, "VALIDATION_ERROR", "Tanggal check-in tidak boleh di masa lalu.");
        if (req.CheckOut <= req.CheckIn)
            throw new BusinessException(400, "VALIDATION_ERROR", "Tanggal check-out harus setelah check-in.");
        if (req.GuestCount <= 0 || req.GuestCount > room.Capacity)
            throw new BusinessException(400, "VALIDATION_ERROR", $"Jumlah guest tidak sesuai kapasitas kamar (maks {room.Capacity}).");
        if (string.IsNullOrWhiteSpace(req.GuestName))
            throw new BusinessException(400, "VALIDATION_ERROR", "Nama guest wajib diisi.");
        // UAT-BKG-07: tolak bila harga yang dilihat Customer saat pencarian sudah tidak berlaku.
        if (req.ExpectedPricePerNight.HasValue && req.ExpectedPricePerNight.Value != room.PricePerNight)
            throw new BusinessException(409, "PRICE_CHANGED",
                $"Harga kamar telah berubah dari {req.ExpectedPricePerNight.Value:N0} menjadi {room.PricePerNight:N0}. Silakan ulangi pencarian.");

        var booking = new Booking();

        // Lock per kamar untuk mencegah double booking pada request bersamaan (TSD 9.2).
        lock (_locks.GetOrAdd(req.RoomId, _ => new object()))
        {
            if (_hotels.AvailableRooms(req.RoomId, req.CheckIn, req.CheckOut) <= 0)
                throw new BusinessException(409, "ROOM_UNAVAILABLE", "Kamar tidak tersedia untuk periode tersebut.");

            var nights = req.CheckOut.DayNumber - req.CheckIn.DayNumber;
            booking = new Booking
            {
                BookingNumber = $"BK-{DateTime.UtcNow:yyyyMMdd}-{RandomNumberGenerator.GetInt32(0, 0xFFFFFF):X6}",
                CustomerId = customerId,
                HotelId = hotel.HotelId,
                RoomId = room.RoomId,
                HotelName = hotel.Name,
                HotelLocation = hotel.Location,
                RoomName = room.Name,
                GuestName = req.GuestName,
                CheckIn = req.CheckIn,
                CheckOut = req.CheckOut,
                GuestCount = req.GuestCount,
                TotalAmount = room.PricePerNight * nights,
                Currency = room.Currency,
                Status = BookingStatus.PendingPayment,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes)
            };
            _db.Bookings[booking.BookingId] = booking;
        }

        _db.AddAudit("Booking", booking.BookingId, "CREATED", null, booking.BookingNumber, customerId.ToString());
        return ToDto(booking);
    }

    public Booking GetOwned(Guid bookingId, Guid customerId)
    {
        var booking = _db.Bookings.GetValueOrDefault(bookingId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Booking tidak ditemukan.");
        if (booking.CustomerId != customerId)
            throw new BusinessException(403, "FORBIDDEN", "Booking bukan milik Anda.");
        return booking;
    }

    public BookingDto Get(Guid bookingId, Guid customerId) => ToDto(GetOwned(bookingId, customerId));

    public PageResult<BookingDto> History(Guid customerId, string group, int page, int size)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var query = _db.Bookings.Values.Where(b => b.CustomerId == customerId);

        query = group?.ToLowerInvariant() switch
        {
            "inprogress" => query.Where(b => b.Status == BookingStatus.Paid && b.CheckOut >= today),
            "completed" => query.Where(b => b.CheckOut < today && (b.Status == BookingStatus.Paid || b.Status == BookingStatus.Cancelled)),
            _ => query.Where(b => b.Status == BookingStatus.Paid)
        };

        var items = query.OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(ToDto).ToList();
        return new PageResult<BookingDto>(items, query.Count(), page, size);
    }

    public static string EnumText<T>(T value) where T : Enum =>
        System.Text.RegularExpressions.Regex.Replace(value.ToString(), "(?<=[a-z])(?=[A-Z])", "_").ToUpperInvariant();

    public static BookingDto ToDto(Booking b) => new(
        b.BookingId, b.BookingNumber, b.HotelName, b.HotelLocation, b.RoomName, b.GuestName,
        b.CheckIn, b.CheckOut, b.GuestCount, b.TotalAmount, b.Currency,
        EnumText(b.Status), b.ExpiresAt, b.CreatedAt, b.PaidAt, b.CancelledAt, b.ExpiredAt);
}
