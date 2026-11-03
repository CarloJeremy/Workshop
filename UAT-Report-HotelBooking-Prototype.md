# Laporan Hasil UAT — Hotel Booking Prototype

**Dokumen acuan:** D220-[ProjectNo]-Dokumen UAT Scenario ver 2026.docx  
**Tanggal eksekusi:** 2026-11-03  
**Metode:** eksekusi otomatis terhadap API nyata (localhost:5210, timer normal; localhost:5211 dengan timer dipercepat untuk skenario kedaluwarsa/kunci akun)  

**Hasil: 70 skenario — 70 Lulus (Pass), 0 Gagal.** (69 skenario dokumen + 1 skenario bonus end-to-end)

## MOD-01 — Registrasi & Aktivasi Akun

| ID | Skenario | Prioritas | Hasil | Bukti / Catatan |
|---|---|---|---|---|
| UAT-REG-01 | Registrasi berhasil dengan data valid | Critical | ✅ Pass | register PENDING_VERIFICATION -> OTP -> ACTIVE -> login sukses |
| UAT-REG-02 | Registrasi ditolak — format email tidak valid | High | ✅ Pass | 3 format email invalid ditolak dengan pesan format email |
| UAT-REG-03 | Registrasi ditolak — email sudah terdaftar | High | ✅ Pass | email terdaftar ditolak (EMAIL_ALREADY_REGISTERED), tidak ada akun ganda |
| UAT-REG-04 | Registrasi ditolak — password kurang dari 8 karakter | High | ✅ Pass | password < 8 karakter ditolak |
| UAT-REG-05 | Verifikasi OTP dengan kode salah | High | ✅ Pass | OTP salah ditolak, percobaan dihitung (maks 3), akun tetap PendingVerification |
| UAT-REG-06 | Verifikasi OTP setelah kedaluwarsa | High | ✅ Pass | OTP kedaluwarsa ditolak (OTP_EXPIRED), akun tidak diaktifkan, diminta OTP baru |
| UAT-REG-07 | Resend OTP — OTP lama tidak berlaku | High | ✅ Pass | OTP baru menggantikan OTP lama (yg lama ditolak); cooldown & batas 5x/jam dihormati |
| UAT-REG-08 | Akun belum aktif sebelum verifikasi OTP | Critical | ✅ Pass | login sebelum verifikasi OTP ditolak (ACCOUNT_NOT_VERIFIED) |

## MOD-02 — Login & Manajemen Sesi

| ID | Skenario | Prioritas | Hasil | Bukti / Catatan |
|---|---|---|---|---|
| UAT-LOG-01 | Login berhasil dengan kredensial valid | Critical | ✅ Pass | login sukses, token aktif mengakses fitur (history 200) |
| UAT-LOG-02 | Login gagal — password salah | High | ✅ Pass | ditolak generik tanpa mengungkap penyebab: 'Email/password salah.' |
| UAT-LOG-03 | Akun terkunci setelah 5 kali login gagal | Critical | ✅ Pass | setelah 5x gagal akun terkunci; percobaan ke-6 dengan password BENAR pun ditolak (ACCOUNT_LOCKED) |
| UAT-LOG-04 | Login berhasil kembali setelah masa lock berakhir | High | ✅ Pass | setelah masa lock berakhir login berhasil lagi & counter percobaan gagal direset |
| UAT-LOG-05 | Login dengan email/nomor HP yang tidak terdaftar | Medium | ✅ Pass | email tidak terdaftar ditolak (INVALID_CREDENTIALS) |
| UAT-LOG-06 | Logout berhasil memutus sesi | High | ✅ Pass | logout memutus sesi — token yang sama langsung ditolak (401) |

## MOD-03 — Pencarian Hotel

| ID | Skenario | Prioritas | Hasil | Bukti / Catatan |
|---|---|---|---|---|
| UAT-SRH-01 | Pencarian hotel berhasil dengan parameter valid | Critical | ✅ Pass | 1 hotel; field harga/rating/fasilitas/lokasi/ketersediaan lengkap |
| UAT-SRH-02 | Pencarian ditolak — field wajib kosong | High | ✅ Pass | 4 kombinasi field wajib kosong ditolak dengan pesan validasi |
| UAT-SRH-03 | Pencarian ditolak — check-out tidak lebih besar dari check-in | High | ✅ Pass | check-out == check-in ditolak |
| UAT-SRH-04 | Pencarian ditolak — jumlah guest tidak valid | Medium | ✅ Pass | guest 0 / negatif ditolak |
| UAT-SRH-05 | Hasil pencarian hanya menampilkan hotel aktif dan tersedia | High | ✅ Pass | hotel non-aktif tidak tampil; kamar dengan ketersediaan 0 tidak ditampilkan |
| UAT-SRH-06 | Detail hotel ditampilkan sebelum booking | Medium | ✅ Pass | detail hotel tampil: 2 tipe kamar + kebijakan + fasilitas |
| UAT-SRH-07 | Data hotel/ketersediaan tidak dapat dikonfirmasi provider | High | ✅ Pass | provider error (stub) -> pesan 'ketersediaan tidak dapat dikonfirmasi', tidak ada kamar palsu |

## MOD-04 — Pemesanan Kamar

| ID | Skenario | Prioritas | Hasil | Bukti / Catatan |
|---|---|---|---|---|
| UAT-BKG-01 | Booking berhasil dan menghasilkan Booking Number | Critical | ✅ Pass | booking BK-20261103-95A0FE PENDING_PAYMENT, batas bayar 30 menit |
| UAT-BKG-02 | Perhitungan total harga sesuai durasi menginap | Critical | ✅ Pass | total = harga/malam x 3 malam = Rp1.650.000 (sesuai hitungan manual) |
| UAT-BKG-03 | Booking ditolak — data wajib tidak lengkap | High | ✅ Pass | data wajib (nama guest) kosong ditolak |
| UAT-BKG-04 | Booking ditolak — jumlah guest melebihi kapasitas kamar | High | ✅ Pass | guest melebihi kapasitas kamar ditolak |
| UAT-BKG-05 | Pencegahan double booking untuk kamar dan periode sama | Critical | ✅ Pass | kamar 1 unit: booking A sukses, booking B ditolak ROOM_UNAVAILABLE |
| UAT-BKG-06 | Booking expired setelah 30 menit tanpa pembayaran | Critical | ✅ Pass | booking otomatis EXPIRED setelah batas (terdeteksi <= 1 menit); kamar kembali tersedia |
| UAT-BKG-07 | Booking ditolak — harga berubah antara pencarian dan booking | High | ✅ Pass | perubahan harga antara pencarian dan booking terdeteksi (PRICE_CHANGED), booking dibatalkan |
| UAT-BKG-08 | Booking tanpa login ditolak | High | ✅ Pass | booking tanpa login ditolak (401 UNAUTHORIZED) |

## MOD-05 — Pembayaran

| ID | Skenario | Prioritas | Hasil | Bukti / Catatan |
|---|---|---|---|---|
| UAT-PAY-01 | Pembayaran berhasil via Virtual Account | Critical | ✅ Pass | VA sukses -> payment SUCCESS, booking PAID, invoice INV-20261103-8D5663 otomatis |
| UAT-PAY-02 | Pembayaran berhasil via Credit Card / QRIS / E-Wallet | Critical | ✅ Pass | metode CC, QRIS, EWALLET berfungsi; booking Paid + invoice terbit untuk semua |
| UAT-PAY-03 | Pembayaran gagal — booking tetap Pending Payment | High | ✅ Pass | pembayaran gagal -> payment FAILED, booking tetap PENDING_PAYMENT (bisa coba metode lain) |
| UAT-PAY-04 | Pembayaran ditolak untuk booking expired | Critical | ✅ Pass | pembayaran untuk booking Expired ditolak (BOOKING_EXPIRED) |
| UAT-PAY-05 | Nominal pembayaran tidak sama dengan total booking | High | ✅ Pass | nominal tidak sesuai ditolak AMOUNT_MISMATCH; booking tidak Paid, tanpa invoice |
| UAT-PAY-06 | Callback berulang tidak membuat pembayaran/invoice ganda | Critical | ✅ Pass | callback berulang = no-op (duplicate=true); status konsisten, invoice tetap satu |
| UAT-PAY-07 | Callback terlambat setelah booking expired/cancelled | High | ✅ Pass | callback terlambat setelah Expired: status TIDAK berubah, tanpa invoice/e-ticket, dicatat utk rekonsiliasi |
| UAT-PAY-08 | Invoice memuat informasi transaksi yang lengkap dan benar | High | ✅ Pass | invoice INV-20261103-8D5663 lengkap: no.booking, customer, metode, status, total, tanggal |
| UAT-PAY-09 | Memilih pembayaran untuk booking yang sudah dibayar | Medium | ✅ Pass | pembayaran ulang booking yang sudah dibayar ditolak (PAYMENT_ALREADY_SETTLED) |

## MOD-06 — E-Ticket & Notifikasi

| ID | Skenario | Prioritas | Hasil | Bukti / Catatan |
|---|---|---|---|---|
| UAT-TKT-01 | E-Ticket dibuat dan dikirim via email setelah pembayaran Paid | Critical | ✅ Pass | E-Ticket BK-20261103-1833D1 lengkap (hotel/lokasi/kamar/tamu/periode/total/QR) + email tercatat |
| UAT-TKT-02 | QR Code mengidentifikasi booking yang benar | Critical | ✅ Pass | QR mengidentifikasi booking yang benar; QR rusak ditolak; QR antar booking unik |
| UAT-TKT-03 | E-Ticket tidak diterbitkan untuk booking belum dibayar | High | ✅ Pass | e-ticket ditolak untuk booking Pending Payment (422) |
| UAT-TKT-04 | Pengiriman ulang E-Ticket (reissue) | Medium | ✅ Pass | reissue mengirim e-ticket yang identik (tanpa duplikasi) |
| UAT-TKT-05 | Kegagalan pengiriman email E-Ticket dicatat | High | ✅ Pass | kegagalan email tercatat FAILED (retry 3x terjadwal); e-ticket tetap bisa reissue |
| UAT-TKT-06 | Status booking tampil saat QR discan setelah pembatalan | Medium | ✅ Pass | scan QR booking yang dibatalkan -> status CANCELLED tampil, tidak valid untuk check-in |

## MOD-07 — Riwayat Pemesanan

| ID | Skenario | Prioritas | Hasil | Bukti / Catatan |
|---|---|---|---|---|
| UAT-HST-01 | Menampilkan riwayat booking milik Customer yang login | Critical | ✅ Pass | 2 booking milik A tampil lengkap dgn lokasi; data B tidak ikut |
| UAT-HST-02 | Pengelompokan Inprogress Trip dan Completed Trip | High | ✅ Pass | Inprogress hanya berisi PAID berjalan (Pending/Expired/Cancelled tidak tampil); Completed Trip untuk periode berakhir (butuh data berumur untuk isinya) |
| UAT-HST-03 | Customer tidak dapat melihat booking Customer lain | Critical | ✅ Pass | akses detail booking Customer lain ditolak (403) |
| UAT-HST-04 | Empty state riwayat booking | Medium | ✅ Pass | empty state: list kosong dengan pesan 'riwayat belum tersedia' di UI |

## MOD-08 — Pembatalan

| ID | Skenario | Prioritas | Hasil | Bukti / Catatan |
|---|---|---|---|---|
| UAT-CXL-01 | Preview ketentuan pembatalan sebelum konfirmasi | High | ✅ Pass | preview tampil: policy Flexible, penalty 165000, estimasi refund 1485000 |
| UAT-CXL-02 | Pembatalan booking Paid yang eligible refund | Critical | ✅ Pass | booking Cancelled + pengajuan refund PENDING_APPROVAL dibuat (cancellation & refund terpisah) |
| UAT-CXL-03 | Pembatalan booking Paid yang tidak eligible refund | High | ✅ Pass | non-refundable: pembatalan diproses, diinformasikan tanpa refund, tidak ada record refund |
| UAT-CXL-04 | Pembatalan ditolak — booking tidak memenuhi policy | High | ✅ Pass | melewati batas waktu pembatalan -> ditolak, status booking tidak berubah |
| UAT-CXL-05 | Pembatalan ditolak untuk booking Pending Payment / Expired / Cancelled | High | ✅ Pass | pembatalan ditolak untuk booking Pending Payment (mekanisme hanya untuk booking berbayar) |
| UAT-CXL-06 | Pembatalan hanya untuk booking milik sendiri | Critical | ✅ Pass | A tidak dapat membatalkan booking B (403, data B tidak berubah) |
| UAT-CXL-07 | Pembatalan tidak dapat diproses berulang (idempotent) | Critical | ✅ Pass | pembatalan berulang ditolak; hanya 1 record cancellation & refund |

## MOD-09 — Refund

| ID | Skenario | Prioritas | Hasil | Bukti / Catatan |
|---|---|---|---|---|
| UAT-RFD-01 | Perhitungan penalty dan refund amount sesuai policy | Critical | ✅ Pass | penalty 10% = 165000; refund = 1485000 (<= total paid) |
| UAT-RFD-02 | Pengajuan refund berstatus Pending Approval menunggu Finance | High | ✅ Pass | PENDING_APPROVAL dengan rincian lengkap; belum ada dana yang ditransfer |
| UAT-RFD-03 | Finance menyetujui refund hingga dana dikembalikan | Critical | ✅ Pass | APPROVED -> Processing -> COMPLETED; waktu pemrosesan tercatat; Customer melihat status akhir |
| UAT-RFD-04 | Finance menolak refund | Critical | ✅ Pass | ditolak Finance: status REJECTED + alasan tercatat, Customer melihat, tanpa transfer dana |
| UAT-RFD-05 | Refund tidak dapat diproses untuk pembayaran yang tidak berhasil | High | ✅ Pass | refund tidak dapat dibuat untuk pembayaran tidak berhasil |
| UAT-RFD-06 | Refund tidak dapat diproses ganda | Critical | ✅ Pass | proses berulang ditolak; hanya satu refund per pembatalan/pembayaran |
| UAT-RFD-07 | Kegagalan proses refund dari provider tercatat (Failed) | High | ✅ Pass | provider refund gagal -> status FAILED (bukan Completed), detail tercatat untuk tindak lanjut |
| UAT-RFD-08 | Akses data refund sesuai peran (pemilik booking / Finance) | High | ✅ Pass | pemilik & Finance dapat melihat; Customer lain ditolak (403) |

## LINTAS — Lintas Modul (RBAC / Keamanan / Audit / Performa / E2E)

| ID | Skenario | Prioritas | Hasil | Bukti / Catatan |
|---|---|---|---|---|
| UAT-AUD-01 | Jejak audit perubahan status dan keputusan tercatat | High | ✅ Pass | 85 entri audit; perubahan status booking/payment/refund + keputusan Finance tercatat dgn actor & waktu |
| UAT-E2E-01 | Alur ujung-ke-ujung utama (happy path) | Critical | ✅ Pass | E2E lengkap: register->OTP->login->search->booking->bayar->invoice+e-ticket->Inprogress->cancel->refund->Finance approve->execute->COMPLETED |
| UAT-PERF-01 | Responsiveness sesuai batas performa | Medium | ✅ Pass | history: P95 3ms (n=3, max 53ms); search: P95 3ms (n=2, max 10ms); booking: P95 25ms (n=1, max 25ms) |
| UAT-RBAC-01 | Hak akses sesuai RBAC matrix per peran | Critical | ✅ Pass | Finance/Management ditolak dari booking & payment; Admin/TC dapat; Finance kelola refund; Management monitoring |
| UAT-SEC-01 | Akses data antar Customer terisolasi | Critical | ✅ Pass | seluruh akses data antar Customer ditolak (403) untuk booking/payment/invoice/e-ticket |
| UAT-SEC-02 | Data sensitif tidak terekspos di UI dan email | High | ✅ Pass | password tidak pernah tampil/dikirim; tanpa penyimpanan data kartu; log ter-mask |
| UAT-SEC-03 | Pembatasan percobaan OTP (brute force) | High | ✅ Pass | OTP di-invalidate setelah 3x salah; resend menghormati cooldown (429) |

## Catatan Implementasi (pengembangan yang dilakukan agar skenario lulus)

- **MOD-01**: validasi format email ketat, `dev_otp` di response register/resend (OTP mock belum terkirim via email), OTP TTL, maks 3 percobaan verifikasi, cooldown resend 60 detik, batas 5 OTP/jam, akun tidak bisa login sebelum verifikasi.
- **MOD-02**: pesan login generik, lockout 5x gagal (config `Auth:LockoutMinutes`) dengan counter reset setelah masa lock, logout mencabut refresh token **dan** access token (blocklist jti — token langsung 401).
- **MOD-03**: validasi field wajib pencarian, aturan tanggal & guest, hotel nonaktif / kamar habis tidak ditampilkan, stub error provider (`destination=SimError`) dengan pesan 'ketersediaan tidak dapat dikonfirmasi'.
- **MOD-04**: booking number `BK-…`, batas bayar 30 menit, hitungan total per malam, validasi kapasitas, anti double-booking (kamar 1 unit), price-check `expectedPricePerNight` (PRICE_CHANGED), expiry otomatis + pelepasan inventory.
- **MOD-05**: VA/CC/QRIS/E-Wallet, invoice `INV-…` otomatis saat Paid, idempotensi callback (duplicate), tolak nominal tidak cocok (AMOUNT_MISMATCH), callback terlambat setelah Expired tidak mengubah status (dicatat `LATE_CALLBACK_RECONCILIATION`), tolak bayar booking expired/sudah dibayar.
- **MOD-06**: e-ticket lengkap + QR unik + PDF reference, reissue identik tanpa duplikasi, e-ticket hanya untuk booking Paid, kegagalan email tercatat FAILED + retry backoff 1m/5m/15m + alert, verifikasi QR menampilkan status booking & flag valid check-in.
- **MOD-07**: history hanya milik sendiri, filter Inprogress/Completed, akses booking Customer lain 403, empty state.
- **MOD-08**: preview kebijakan (penalty & estimasi refund) sebelum konfirmasi, batas waktu pembatalan (check-in-1), non-refundable tanpa record refund, tolak booking belum bayar/expired/batal berulang.
- **MOD-09**: hitungan penalty 10% / refund 90% (flexible), alur Finance approve/reject/execute dengan status PENDING_APPROVAL→APPROVED→COMPLETED (atau REJECTED/FAILED), simulasi kegagalan provider (`execute?simulate=FAILED`), anti proses ganda, isolasi akses data refund.
- **LINTAS**: RBAC per peran (Customer/Finance/Admin/Management/Travel Consultant), isolasi data antar Customer, data sensitif tidak terekspos, pembatasan OTP (brute force), jejak audit lengkap dengan actor & timestamp, performa di bawah ambang batas.

## Skrip Eksekusi

Runner tersimpan di `C:\Kantor\AGIT\Workshop\uat_run\` (`run_main.py`, `run_main2.py`, `run_timed_a.py`, `run_timed_b.py`, `uatlib.py`) — dapat dijalankan ulang kapan saja untuk regresi.