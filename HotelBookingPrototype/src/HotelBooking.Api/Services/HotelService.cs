using HotelBooking.Api.Data;
using HotelBooking.Api.Dtos;
using HotelBooking.Api.Infrastructure;
using HotelBooking.Api.Models;

namespace HotelBooking.Api.Services;

public class HotelService
{
    private readonly InMemoryDb _db;

    public HotelService(InMemoryDb db) => _db = db;

    public List<HotelSearchResult> Search(string? destination, DateOnly? checkIn, DateOnly? checkOut, int? guestCount)
    {
        Validate(checkIn, checkOut, guestCount);
        var ci = checkIn!.Value;
        var co = checkOut!.Value;
        var gc = guestCount ?? 0; // null = tanpa filter kapasitas (tampilkan semua)

        // Stub provider (UAT-SRH-07): destinasi "SimError" mensimulasikan Hotel API provider error/timeout.
        if (!string.IsNullOrWhiteSpace(destination) &&
            destination.Trim().Equals("SimError", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException(502, "PROVIDER_ERROR",
                "Ketersediaan tidak dapat dikonfirmasi saat ini. Silakan coba lagi nanti.");

        var results = new List<HotelSearchResult>();
        foreach (var hotel in _db.Hotels.Values.Where(h =>
                     h.IsActive && (string.IsNullOrWhiteSpace(destination) ||
                                    h.City.Contains(destination, StringComparison.OrdinalIgnoreCase) ||
                                    h.Name.Contains(destination, StringComparison.OrdinalIgnoreCase))))
        {
            var rooms = _db.Rooms.Values
                .Where(r => r.HotelId == hotel.HotelId && r.Capacity >= gc)
                .Select(r => new RoomAvailabilityDto(
                    r.RoomId, r.Name, r.Capacity, r.PricePerNight, r.Currency,
                    AvailableRooms(r.RoomId, ci, co), r.Policy))
                .Where(r => r.AvailableRooms > 0)
                .ToList();

            if (rooms.Count > 0)
                results.Add(new HotelSearchResult(hotel.HotelId, hotel.Name, hotel.City, hotel.Location,
                    hotel.Rating, hotel.Facilities, rooms));
        }
        return results;
    }

    public HotelSearchResult Get(string hotelId, DateOnly? checkIn, DateOnly? checkOut, int? guestCount)
    {
        Validate(checkIn, checkOut, guestCount);
        var hotel = _db.Hotels.GetValueOrDefault(hotelId)
            ?? throw new BusinessException(404, "NOT_FOUND", "Hotel tidak ditemukan.");
        var rooms = _db.Rooms.Values
            .Where(r => r.HotelId == hotelId)
            .Select(r => new RoomAvailabilityDto(r.RoomId, r.Name, r.Capacity, r.PricePerNight, r.Currency,
                AvailableRooms(r.RoomId, checkIn!.Value, checkOut!.Value), r.Policy))
            .ToList();
        return new HotelSearchResult(hotel.HotelId, hotel.Name, hotel.City, hotel.Location, hotel.Rating, hotel.Facilities, rooms);
    }

    /// <summary>Validasi parameter pencarian: tanggal wajib diisi; destinasi opsional (kosong = semua destinasi);
    /// jumlah tamu opsional (kosong = tampilkan semua hotel/kamar tanpa filter kapasitas).</summary>
    private static void Validate(DateOnly? checkIn, DateOnly? checkOut, int? guestCount)
    {
        if (checkIn is null)
            throw new BusinessException(400, "VALIDATION_ERROR", "Tanggal check-in wajib diisi.");
        if (checkOut is null)
            throw new BusinessException(400, "VALIDATION_ERROR", "Tanggal check-out wajib diisi.");
        if (checkOut <= checkIn)
            throw new BusinessException(400, "VALIDATION_ERROR", "Tanggal check-out harus setelah check-in.");
        if (guestCount is <= 0)
            throw new BusinessException(400, "VALIDATION_ERROR", "Jumlah guest harus angka positif yang valid.");
    }

    /// <summary>Kamar tersedia = total kamar - booking aktif (PendingPayment/Paid) yang periodenya tumpang tindih.</summary>
    public int AvailableRooms(string roomId, DateOnly checkIn, DateOnly checkOut)
    {
        var room = _db.Rooms.GetValueOrDefault(roomId);
        if (room == null) return 0;
        var overlapping = _db.Bookings.Values.Count(b =>
            b.RoomId == roomId &&
            (b.Status == BookingStatus.PendingPayment || b.Status == BookingStatus.Paid) &&
            b.CheckIn < checkOut && b.CheckOut > checkIn);
        return Math.Max(0, room.TotalRooms - overlapping);
    }
}
