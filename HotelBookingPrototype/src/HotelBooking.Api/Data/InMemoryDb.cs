using System.Collections.Concurrent;
using HotelBooking.Api.Models;

namespace HotelBooking.Api.Data;

/// <summary>
/// Penyimpanan in-memory untuk prototipe. Pada implementasi produksi digantikan
/// oleh PostgreSQL (lihat TSD bagian 6 - Data Model).
/// </summary>
public class InMemoryDb
{
    public ConcurrentDictionary<Guid, Customer> Customers { get; } = new();
    public ConcurrentDictionary<Guid, OtpVerification> Otps { get; } = new();
    public ConcurrentDictionary<Guid, RefreshToken> RefreshTokens { get; } = new();
    public ConcurrentDictionary<string, Hotel> Hotels { get; } = new();
    public ConcurrentDictionary<string, Room> Rooms { get; } = new();
    public ConcurrentDictionary<Guid, Booking> Bookings { get; } = new();
    public ConcurrentDictionary<Guid, Payment> Payments { get; } = new();
    public ConcurrentDictionary<Guid, Invoice> Invoices { get; } = new();
    public ConcurrentDictionary<Guid, ETicket> ETickets { get; } = new();
    public ConcurrentDictionary<Guid, Cancellation> Cancellations { get; } = new();
    public ConcurrentDictionary<Guid, Refund> Refunds { get; } = new();
    public ConcurrentDictionary<Guid, NotificationLog> Notifications { get; } = new();
    public ConcurrentDictionary<Guid, AuditLog> AuditLogs { get; } = new();

    /// <summary>Daftar jti access token yang sudah dicabut (logout) — untuk UAT-LOG-06.</summary>
    public ConcurrentDictionary<string, DateTime> RevokedAccessJtis { get; } = new();

    public InMemoryDb()
    {
        Seed();
    }

    public void AddAudit(string entity, Guid entityId, string action, string? oldValue, string? newValue, string actor)
    {
        var log = new AuditLog { Entity = entity, EntityId = entityId, Action = action, OldValue = oldValue, NewValue = newValue, Actor = actor };
        AuditLogs[log.Id] = log;
    }

    private void Seed()
    {
        // Demo users (password: Demo1234!)
        void SeedUser(string email, string name, string role, AccountStatus status)
        {
            var c = new Customer
            {
                Email = email,
                FullName = name,
                Role = role,
                AccountStatus = status,
                PasswordHash = Infrastructure.PasswordHasher.HashStatic("Demo1234!")
            };
            Customers[c.CustomerId] = c;
        }
        SeedUser("customer@demo.com", "Demo Customer", "Customer", AccountStatus.Active);
        SeedUser("finance@demo.com", "Demo Finance", "Finance", AccountStatus.Active);
        SeedUser("admin@demo.com", "Demo Admin", "Admin", AccountStatus.Active);
        SeedUser("management@demo.com", "Demo Management", "Management", AccountStatus.Active);
        SeedUser("travel@demo.com", "Demo Travel Consultant", "TravelConsultant", AccountStatus.Active);

        // Hotels & rooms
        var hotels = new[]
        {
            new Hotel { HotelId = "HTL-JKT-001", Name = "Astra Grand Hotel", City = "Jakarta", Location = "Jl. Jend. Sudirman, Jakarta", Rating = 4.6, Facilities = new() { "WiFi", "Kolam Renang", "Gym", "Restoran", "Parkir" } },
            new Hotel { HotelId = "HTL-DPS-001", Name = "Ubud Paradise Resort", City = "Bali", Location = "Jl. Raya Ubud, Bali", Rating = 4.8, Facilities = new() { "WiFi", "Spa", "Kolam Renang", "Restoran" } },
            new Hotel { HotelId = "HTL-BDG-001", Name = "Bandung Highland Hotel", City = "Bandung", Location = "Jl. Asia Afrika, Bandung", Rating = 4.3, Facilities = new() { "WiFi", "Parkir", "Restoran" } },
            // UAT-SRH-05: hotel non-aktif tidak boleh muncul di hasil pencarian.
            new Hotel { HotelId = "HTL-JKT-002", Name = "Grand Old Hotel (Nonaktif)", City = "Jakarta", Location = "Jl. Veteran, Jakarta", Rating = 3.0, Facilities = new() { "WiFi" }, IsActive = false },
        };
        foreach (var h in hotels) Hotels[h.HotelId] = h;

        var rooms = new[]
        {
            new Room { RoomId = "RM-JKT-STD", HotelId = "HTL-JKT-001", Name = "Deluxe Room", Capacity = 2, PricePerNight = 850000m, TotalRooms = 10, Policy = "Flexible", PenaltyRate = 0.10m },
            new Room { RoomId = "RM-JKT-SUI", HotelId = "HTL-JKT-001", Name = "Executive Suite", Capacity = 4, PricePerNight = 1750000m, TotalRooms = 5, Policy = "Flexible", PenaltyRate = 0.10m },
            // UAT-BKG-05: kamar dengan stok 1 unit untuk pengujian anti double-booking.
            new Room { RoomId = "RM-JKT-HNY", HotelId = "HTL-JKT-001", Name = "Honeymoon Suite (1 unit)", Capacity = 2, PricePerNight = 3200000m, TotalRooms = 1, Policy = "Flexible", PenaltyRate = 0.10m },
            new Room { RoomId = "RM-DPS-VIL", HotelId = "HTL-DPS-001", Name = "Private Villa", Capacity = 4, PricePerNight = 2500000m, TotalRooms = 4, Policy = "NonRefundable", PenaltyRate = 1.00m },
            new Room { RoomId = "RM-DPS-STD", HotelId = "HTL-DPS-001", Name = "Garden Room", Capacity = 2, PricePerNight = 1200000m, TotalRooms = 8, Policy = "Flexible", PenaltyRate = 0.10m },
            new Room { RoomId = "RM-BDG-STD", HotelId = "HTL-BDG-001", Name = "Standard Room", Capacity = 2, PricePerNight = 550000m, TotalRooms = 12, Policy = "Flexible", PenaltyRate = 0.10m },
            new Room { RoomId = "RM-BDG-FAM", HotelId = "HTL-BDG-001", Name = "Family Room", Capacity = 5, PricePerNight = 950000m, TotalRooms = 6, Policy = "Flexible", PenaltyRate = 0.10m },
        };
        foreach (var r in rooms) Rooms[r.RoomId] = r;
    }
}
