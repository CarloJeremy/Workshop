# Hotel Booking Prototype (ASP.NET Core 8 Web API)

Prototipe implementasi dari **Dokumen Technical Specification (TSD) — Hotel Booking Platform**.
Data disimpan in-memory (tanpa database) untuk keperluan demo; pada implementasi produksi
digantikan PostgreSQL sesuai TSD bagian 6.

## Cara Membuka di Visual Studio 2022

1. Buka `HotelBookingPrototype.sln` di Visual Studio 2022 (17.8 atau lebih baru).
2. Pastikan workload **ASP.NET and web development** terpasang dan .NET 8 SDK tersedia.
3. Tekan **F5** atau **Ctrl+F5** (tanpa debug). Aplikasi jalan di `http://localhost:5210`.
4. Buka `http://localhost:5210/swagger` untuk UI Swagger interaktif,
   atau gunakan file `src/HotelBooking.Api/HotelBooking.Api.http` (tab .http di VS 2022).

> Catatan: target framework `net8.0` dengan `RollForward=LatestMajor`, sehingga tetap bisa
> berjalan meski yang terpasang runtime .NET 9/10.

## Akun Demo

| Role    | Email             | Password   |
|---------|-------------------|------------|
| Customer| customer@demo.com | Demo1234!  |
| Finance | finance@demo.com  | Demo1234!  |
| Admin   | admin@demo.com    | Demo1234!  |

Registrasi baru (`/api/v1/auth/register`) mencetak OTP ke console aplikasi
(prototipe: email tidak benar-benar dikirim).

## Ringkasan Endpoint (sesuai TSD bagian 4)

| Modul | Endpoint Utama |
|-------|----------------|
| MOD-01 Registration | `POST /api/v1/auth/register`, `/verify-otp`, `/resend-otp` |
| MOD-02 Login | `POST /api/v1/auth/login`, `/refresh`, `/logout` |
| MOD-03 Search Hotel | `GET /api/v1/hotels/search`, `GET /api/v1/hotels/{id}` |
| MOD-04 Booking | `POST /api/v1/bookings`, `GET /api/v1/bookings/history`, `GET /api/v1/bookings/{id}` |
| MOD-05 Payment | `POST /api/v1/payments`, `POST /api/v1/payments/callback` (HMAC X-Signature), `POST /api/v1/payments/{id}/simulate-callback` |
| MOD-06 E-Ticket | `GET /api/v1/etickets/verify?code=`, `POST /api/v1/etickets/reissue` |
| MOD-08 Cancellation | `GET /api/v1/bookings/{id}/cancellation-preview`, `POST /api/v1/bookings/{id}/cancel` |
| MOD-09 Refund | `GET /api/v1/refunds/{id}`, `POST /api/v1/refunds/{id}/approve|reject|execute` (Finance) |
| MOD-10 Admin Panel | `GET /api/v1/admin/dashboard`, `GET|POST /api/v1/admin/hotels`, `PUT /api/v1/admin/hotels/{id}`, `POST /api/v1/admin/hotels/{id}/status`, `POST /api/v1/admin/hotels/{id}/rooms`, `PUT|DELETE /api/v1/admin/rooms/{id}`, `GET /api/v1/admin/bookings`, `GET /api/v1/admin/bookings/recap`, `GET /api/v1/admin/users`, `POST /api/v1/admin/users/{id}/status`, `GET /api/v1/admin/audits`, `GET /api/v1/admin/notifications` |

## Panel Admin (`/admin.html`)

Konsol admin di `http://localhost:5210/admin.html` (login dengan `admin@demo.com` / `Demo1234!`).
Fitur: dashboard statistik, **tambah/edit/nonaktifkan hotel & tipe kamar**, **rekap booking**
(per status, per hotel, tren harian, detail berfilter), persetujuan refund, kelola status
pengguna, serta audit log & log notifikasi. RBAC: tulis katalog/pengguna khusus `Admin`;
`Finance`/`Management` akses baca + approve/reject refund.

## Alur Demo Singkat

1. Login sebagai `customer@demo.com` → ambil `access_token`.
2. `GET /api/v1/hotels/search?destination=Jakarta&checkIn=2026-12-01&checkOut=2026-12-03&guestCount=2`.
3. `POST /api/v1/bookings` → dapat `bookingId` (status `PENDING_PAYMENT`, expired 30 menit).
4. `POST /api/v1/payments` (mis. `VA`) → `paymentId`.
5. `POST /api/v1/payments/{paymentId}/simulate-callback?status=SUCCESS` → booking `PAID`, invoice & E-Ticket dibuat.
6. `POST /api/v1/bookings/{id}/cancel` → refund dibuat (bila policy Flexible & sudah dibayar).
7. Login `finance@demo.com` → `POST /api/v1/refunds/{id}/approve` → `/execute` → status `COMPLETED`.
8. `GET /api/v1/etickets/verify?code=<qrCode>` untuk verifikasi QR.

## Fitur Teknis yang Diimplementasikan (pemetaan TSD)

- **JWT** access token + refresh token rotating (TSD 4.2).
- **OTP** 6 digit, TTL 10 menit, maks 3 percobaan, cooldown resend 60 detik (TSD 4.1).
- **Lockout** 5x login gagal selama 30 menit (TSD 4.2).
- **Anti double booking**: lock per kamar + cek overlap periode (TSD 9.2).
- **Booking expiry job** 30 menit (BackgroundService) (TSD 8).
- **Callback Payment Gateway**: verifikasi HMAC-SHA256 (header `X-Signature`) + idempotensi
  `ExternalTransactionId` (TSD 5.3).
- **Refund**: penalty policy (Flexible 10% / NonRefundable), approval Finance, status machine
  PENDING_APPROVAL → APPROVED → PROCESSING → COMPLETED (TSD 4.9).
- **E-Ticket QR** berisi token HMAC per booking, diverifikasi via `/etickets/verify` (TSD 4.6).
- **Audit log** untuk setiap perubahan status penting (TSD 7.2).
- **Error envelope standar** `{status, code, message}` (TSD 9.1).

## Batasan Prototipe

- Penyimpanan in-memory (hilang saat aplikasi restart) — produksi pakai PostgreSQL.
- Email tidak dikirim sungguhan (dicatat ke `notification_log` + console).
- PDF E-Ticket & invoice direferensikan saja (`pdfReference`), belum digenerate.
- Payment gateway & hotel provider adalah mock internal.
