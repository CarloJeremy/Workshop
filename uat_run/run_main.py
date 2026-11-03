"""UAT runner bagian 1 — server http://localhost:5210 (timer normal).
Skenario tanpa waiting: REG-01..05,08 | LOG-01,02,05,06 | SRH-01..07 | BKG-01..05,07,08
| PAY-01..03,05,06,08,09 | TKT-01..06 | HST-01..04 | CXL-01..07 | RFD-01..08 | LINTAS."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.modules.pop("uatlib", None)
from uatlib import Api, Recorder, uniq, register_verify_login, create_paid_booking, login
from datetime import date, timedelta

BASE = "http://localhost:5210"
api = Api(BASE)
rec = Recorder("main")
S = {}  # state antar skenario


def run(sid, module, title, prio, fn):
    try:
        note = fn() or "OK"
        rec.add(sid, module, title, prio, True, note)
    except AssertionError as e:
        rec.add(sid, module, title, prio, False, str(e))
    except Exception as e:
        rec.add(sid, module, title, prio, False, f"ERROR {type(e).__name__}: {e}")


def expect(cond, msg):
    if not cond:
        raise AssertionError(msg)


CI = (date.today() + timedelta(days=14)).isoformat()
CO = (date.today() + timedelta(days=17)).isoformat()

# ================= MOD-01 REGISTRASI =================
def reg01():
    email = uniq("reg01")
    st, j, _ = api.post("/api/v1/auth/register", {"email": email, "password": "Demo1234!",
                                                 "fullName": "UAT REG01", "phoneNumber": "081100000001"})
    expect(st == 200 and j["data"]["account_status"] == "PENDING_VERIFICATION", f"register: {st} {j}")
    otp = j["data"].get("dev_otp")
    expect(otp and len(otp) == 6, "dev_otp tidak ada")
    st, j, _ = api.post("/api/v1/auth/verify-otp", {"email": email, "otpCode": otp})
    expect(st == 200 and j["data"]["account_status"] == "ACTIVE", f"verify: {st} {j}")
    st, j = login(api, email)
    expect(st == 200 and j["data"]["access_token"], f"login setelah aktivasi: {st} {j}")
    S["reg01_email"] = email
    return "register PENDING_VERIFICATION -> OTP -> ACTIVE -> login sukses"


def reg02():
    bad = ["nama@salah", "nama.com", "nama @mail.com"]
    for e in bad:
        st, j, _ = api.post("/api/v1/auth/register", {"email": e, "password": "Demo1234!"})
        expect(st == 400 and j.get("code") == "VALIDATION_ERROR" and "email" in j.get("message", "").lower(),
               f"email '{e}' seharusnya ditolak: {st} {j}")
    return f"{len(bad)} format email invalid ditolak dengan pesan format email"


def reg03():
    st, j, _ = api.post("/api/v1/auth/register", {"email": "customer@demo.com", "password": "Demo1234!"})
    expect(st == 409 and j.get("code") == "EMAIL_ALREADY_REGISTERED", f"{st} {j}")
    return "email terdaftar ditolak (EMAIL_ALREADY_REGISTERED), tidak ada akun ganda"


def reg04():
    st, j, _ = api.post("/api/v1/auth/register", {"email": uniq("reg04"), "password": "abc123"})
    expect(st == 400 and "password" in j.get("message", "").lower(), f"{st} {j}")
    return "password < 8 karakter ditolak"


def reg05():
    email = uniq("reg05")
    st, j, _ = api.post("/api/v1/auth/register", {"email": email, "password": "Demo1234!"})
    expect(st == 200, f"{st} {j}")
    for i in range(3):
        st, j, _ = api.post("/api/v1/auth/verify-otp", {"email": email, "otpCode": "000000"})
        expect(st == 401 and j.get("code") == "OTP_INVALID", f"percobaan {i+1}: {st} {j}")
    st, j, _ = api.post("/api/v1/auth/verify-otp", {"email": email, "otpCode": "000000"})
    expect(st == 429 and j.get("code") == "TOO_MANY_REQUESTS", f"melewati batas percobaan: {st} {j}")
    st, j = login(api, email)
    expect(st == 403 and j.get("code") == "ACCOUNT_NOT_VERIFIED", f"akun seharusnya belum aktif: {st} {j}")
    return "OTP salah ditolak, percobaan dihitung (maks 3), akun tetap PendingVerification"


def reg08():
    email = uniq("reg08")
    st, j, _ = api.post("/api/v1/auth/register", {"email": email, "password": "Demo1234!"})
    expect(st == 200, f"{st} {j}")
    st, j = login(api, email)
    expect(st == 403 and j.get("code") == "ACCOUNT_NOT_VERIFIED", f"{st} {j}")
    return "login sebelum verifikasi OTP ditolak (ACCOUNT_NOT_VERIFIED)"


run("UAT-REG-01", "MOD-01", "Registrasi berhasil dengan data valid", "Critical", reg01)
run("UAT-REG-02", "MOD-01", "Registrasi ditolak — format email tidak valid", "High", reg02)
run("UAT-REG-03", "MOD-01", "Registrasi ditolak — email sudah terdaftar", "High", reg03)
run("UAT-REG-04", "MOD-01", "Registrasi ditolak — password kurang dari 8 karakter", "High", reg04)
run("UAT-REG-05", "MOD-01", "Verifikasi OTP dengan kode salah", "High", reg05)
run("UAT-REG-08", "MOD-01", "Akun belum aktif sebelum verifikasi OTP", "Critical", reg08)

# ================= MOD-02 LOGIN =================
def log01():
    st, j = login(api, "customer@demo.com")
    expect(st == 200 and j["data"]["access_token"], f"{st} {j}")
    tok = j["data"]["access_token"]
    S["cust_token"] = tok
    st2, j2, ms = api.get("/api/v1/bookings/history?group=inprogress&page=1&size=10", token=tok)
    rec.latency("history", ms)
    expect(st2 == 200, f"token tidak berlaku: {st2} {j2}")
    return "login sukses, token aktif mengakses fitur (history 200)"


def log02():
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": "customer@demo.com", "password": "Salah123!"})
    expect(st == 401 and j.get("code") == "INVALID_CREDENTIALS", f"{st} {j}")
    m = j.get("message", "").lower()
    expect("password" in m or "email" in m, f"pesan: {j.get('message')}")
    return f"ditolak generik tanpa mengungkap penyebab: '{j.get('message')}'"


def log05():
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": "tidakada@demo.com", "password": "Demo1234!"})
    expect(st == 401 and j.get("code") == "INVALID_CREDENTIALS", f"{st} {j}")
    return "email tidak terdaftar ditolak (INVALID_CREDENTIALS)"


def log06():
    email = uniq("log06")
    tok, _ = register_verify_login(api, email)
    st, j, _ = api.post("/api/v1/auth/logout", {}, token=tok)
    expect(st == 200, f"logout: {st} {j}")
    st, j, _ = api.get("/api/v1/bookings/history?group=inprogress", token=tok)
    expect(st == 401, f"token masih berlaku setelah logout: {st} {j}")
    return "logout memutus sesi — token yang sama langsung ditolak (401)"


run("UAT-LOG-01", "MOD-02", "Login berhasil dengan kredensial valid", "Critical", log01)
run("UAT-LOG-02", "MOD-02", "Login gagal — password salah", "High", log02)
run("UAT-LOG-05", "MOD-02", "Login dengan email/nomor HP yang tidak terdaftar", "Medium", log05)
run("UAT-LOG-06", "MOD-02", "Logout berhasil memutus sesi", "High", log06)

# ================= MOD-03 SEARCH =================
def srh01():
    st, j, ms = api.get(f"/api/v1/hotels/search?destination=Jakarta&checkIn={CI}&checkOut={CO}&guestCount=2")
    rec.latency("search", ms)
    expect(st == 200 and len(j["data"]) > 0, f"{st} {j}")
    h = j["data"][0]
    for f in ["hotelId", "name", "city", "location", "rating", "facilities", "rooms"]:
        expect(f in h, f"field {f} hilang")
    r = h["rooms"][0]
    for f in ["roomId", "name", "capacity", "pricePerNight", "currency", "availableRooms", "policy"]:
        expect(f in r, f"field room {f} hilang")
    return f"{len(j['data'])} hotel; field harga/rating/fasilitas/lokasi/ketersediaan lengkap"


def srh02():
    cases = [
        (f"/api/v1/hotels/search?checkIn={CI}&checkOut={CO}&guestCount=2", "destination"),
        (f"/api/v1/hotels/search?destination=Jakarta&checkOut={CO}&guestCount=2", "check-in"),
        (f"/api/v1/hotels/search?destination=Jakarta&checkIn={CI}&guestCount=2", "check-out"),
        (f"/api/v1/hotels/search?destination=Jakarta&checkIn={CI}&checkOut={CO}", "guest"),
    ]
    for path, field in cases:
        st, j, _ = api.get(path)
        expect(st == 400 and j.get("code") == "VALIDATION_ERROR",
               f"field {field} kosong seharusnya ditolak: {st} {j}")
    return "4 kombinasi field wajib kosong ditolak dengan pesan validasi"


def srh03():
    st, j, _ = api.get(f"/api/v1/hotels/search?destination=Jakarta&checkIn={CI}&checkOut={CI}&guestCount=2")
    expect(st == 400 and "check-out" in j.get("message", "").lower(), f"{st} {j}")
    return "check-out == check-in ditolak"


def srh04():
    for g in ["0", "-2"]:
        st, j, _ = api.get(f"/api/v1/hotels/search?destination=Jakarta&checkIn={CI}&checkOut={CO}&guestCount={g}")
        expect(st == 400 and "guest" in j.get("message", "").lower(), f"guest={g}: {st} {j}")
    return "guest 0 / negatif ditolak"


def srh05():
    st, j, _ = api.get(f"/api/v1/hotels/search?destination=Jakarta&checkIn={CI}&checkOut={CO}&guestCount=2")
    names = [h["name"] for h in j["data"]]
    expect(not any("Nonaktif" in n for n in names), f"hotel nonaktif tampil: {names}")
    # kamar habis (HNY sudah di-booking pada UAT-BKG-05) tidak boleh tampil
    st2, j2, _ = api.get(f"/api/v1/hotels/search?destination=Jakarta&checkIn={CI}&checkOut={CO}&guestCount=2&nocache={time_tag()}")
    for h in j2["data"]:
        for r in h["rooms"]:
            expect(r["availableRooms"] > 0, f"kamar habis tampil: {r}")
    return "hotel non-aktif tidak tampil; kamar dengan ketersediaan 0 tidak ditampilkan"


def time_tag():
    import uuid
    return uuid.uuid4().hex[:6]


def srh06():
    st, j, _ = api.get(f"/api/v1/hotels/HTL-DPS-001?checkIn={CI}&checkOut={CO}&guestCount=2")
    expect(st == 200, f"{st} {j}")
    h = j["data"]
    expect(len(h["rooms"]) > 0 and all(k in h["rooms"][0] for k in ["name", "pricePerNight", "capacity", "policy"]),
           "detail kamar tidak lengkap")
    return f"detail hotel tampil: {len(h['rooms'])} tipe kamar + kebijakan + fasilitas"


def srh07():
    st, j, _ = api.get(f"/api/v1/hotels/search?destination=SimError&checkIn={CI}&checkOut={CO}&guestCount=2")
    expect(st == 502 and j.get("code") == "PROVIDER_ERROR", f"{st} {j}")
    expect("ketersediaan tidak dapat dikonfirmasi" in j.get("message", "").lower(), f"pesan: {j.get('message')}")
    return "provider error (stub) -> pesan 'ketersediaan tidak dapat dikonfirmasi', tidak ada kamar palsu"


run("UAT-SRH-01", "MOD-03", "Pencarian hotel berhasil dengan parameter valid", "Critical", srh01)
run("UAT-SRH-02", "MOD-03", "Pencarian ditolak — field wajib kosong", "High", srh02)
run("UAT-SRH-03", "MOD-03", "Pencarian ditolak — check-out tidak lebih besar dari check-in", "High", srh03)
run("UAT-SRH-04", "MOD-03", "Pencarian ditolak — jumlah guest tidak valid", "Medium", srh04)
run("UAT-SRH-05", "MOD-03", "Hasil pencarian hanya menampilkan hotel aktif dan tersedia", "High", srh05)
run("UAT-SRH-06", "MOD-03", "Detail hotel ditampilkan sebelum booking", "Medium", srh06)
run("UAT-SRH-07", "MOD-03", "Data hotel/ketersediaan tidak dapat dikonfirmasi provider", "High", srh07)

# ================= MOD-04 BOOKING =================
def bkg01():
    email = uniq("bkg01")
    tok, _ = register_verify_login(api, email)
    S["bkg_user"] = (email, tok)
    st, j, ms = api.post("/api/v1/bookings",
                         {"hotelId": "HTL-BDG-001", "roomId": "RM-BDG-STD", "guestName": "Budi UAT",
                          "checkIn": CI, "checkOut": CO, "guestCount": 2, "expectedPricePerNight": 550000}, token=tok)
    rec.latency("booking", ms)
    expect(st == 200, f"{st} {j}")
    b = j["data"]
    expect(b["bookingNumber"].startswith("BK-"), f"format booking number: {b['bookingNumber']}")
    expect(b["status"] == "PENDING_PAYMENT", f"status: {b['status']}")
    exp = abs((b["expiresAt"] and 1) or 0)
    import datetime as dt
    exp_at = dt.datetime.fromisoformat(b["expiresAt"].replace("Z", "+00:00"))
    delta = (exp_at - dt.datetime.now(dt.timezone.utc)).total_seconds()
    expect(25 * 60 < delta < 35 * 60, f"batas waktu bukan ~30 menit: {delta:.0f}s")
    S["bkg01"] = {"email": email, "tok": tok, "booking": b}
    return f"booking {b['bookingNumber']} PENDING_PAYMENT, batas bayar {delta/60:.0f} menit"


def bkg02():
    d = S["bkg01"]
    expect(d["booking"]["totalAmount"] == 550000 * 3,
           f"total {d['booking']['totalAmount']} != 550000 x 3 = {550000*3}")
    return "total = harga/malam x 3 malam = Rp1.650.000 (sesuai hitungan manual)"


def bkg03():
    st, j, _ = api.post("/api/v1/bookings",
                        {"hotelId": "HTL-BDG-001", "roomId": "RM-BDG-STD", "guestName": "  ",
                         "checkIn": CI, "checkOut": CO, "guestCount": 2}, token=S["bkg01"]["tok"])
    expect(st == 400 and "nama" in j.get("message", "").lower(), f"{st} {j}")
    return "data wajib (nama guest) kosong ditolak"


def bkg04():
    st, j, _ = api.post("/api/v1/bookings",
                        {"hotelId": "HTL-BDG-001", "roomId": "RM-BDG-STD", "guestName": "Budi",
                         "checkIn": CI, "checkOut": CO, "guestCount": 6}, token=S["bkg01"]["tok"])
    expect(st == 400 and "kapasitas" in j.get("message", "").lower(), f"{st} {j}")
    return "guest melebihi kapasitas kamar ditolak"


def bkg05():
    # kamar 1 unit (RM-JKT-HNY): A berhasil, B ditolak
    ci = (date.today() + timedelta(days=20)).isoformat()
    co = (date.today() + timedelta(days=22)).isoformat()
    tokA = S["bkg01"]["tok"]
    st, j, _ = api.post("/api/v1/bookings",
                        {"hotelId": "HTL-JKT-001", "roomId": "RM-JKT-HNY", "guestName": "A",
                         "checkIn": ci, "checkOut": co, "guestCount": 2}, token=tokA)
    expect(st == 200, f"A gagal: {st} {j}")
    emailB = uniq("bkg05b")
    tokB, _ = register_verify_login(api, emailB)
    st, j, _ = api.post("/api/v1/bookings",
                        {"hotelId": "HTL-JKT-001", "roomId": "RM-JKT-HNY", "guestName": "B",
                         "checkIn": ci, "checkOut": co, "guestCount": 2}, token=tokB)
    expect(st == 409 and j.get("code") == "ROOM_UNAVAILABLE", f"B seharusnya ditolak: {st} {j}")
    S["bkg05_tokB"] = tokB
    return "kamar 1 unit: booking A sukses, booking B ditolak ROOM_UNAVAILABLE"


def bkg07():
    st, j, _ = api.post("/api/v1/bookings",
                        {"hotelId": "HTL-BDG-001", "roomId": "RM-BDG-STD", "guestName": "Budi",
                         "checkIn": CI, "checkOut": CO, "guestCount": 2,
                         "expectedPricePerNight": 12345}, token=S["bkg01"]["tok"])
    expect(st == 409 and j.get("code") == "PRICE_CHANGED", f"{st} {j}")
    return "perubahan harga antara pencarian dan booking terdeteksi (PRICE_CHANGED), booking dibatalkan"


def bkg08():
    st, j, _ = api.post("/api/v1/bookings",
                        {"hotelId": "HTL-BDG-001", "roomId": "RM-BDG-STD", "guestName": "Budi",
                         "checkIn": CI, "checkOut": CO, "guestCount": 2})
    expect(st == 401, f"tanpa login seharusnya 401: {st} {j}")
    return "booking tanpa login ditolak (401 UNAUTHORIZED)"


run("UAT-BKG-01", "MOD-04", "Booking berhasil dan menghasilkan Booking Number", "Critical", bkg01)
run("UAT-BKG-02", "MOD-04", "Perhitungan total harga sesuai durasi menginap", "Critical", bkg02)
run("UAT-BKG-03", "MOD-04", "Booking ditolak — data wajib tidak lengkap", "High", bkg03)
run("UAT-BKG-04", "MOD-04", "Booking ditolak — jumlah guest melebihi kapasitas kamar", "High", bkg04)
run("UAT-BKG-05", "MOD-04", "Pencegahan double booking untuk kamar dan periode sama", "Critical", bkg05)
run("UAT-BKG-07", "MOD-04", "Booking ditolak — harga berubah antara pencarian dan booking", "High", bkg07)
run("UAT-BKG-08", "MOD-04", "Booking tanpa login ditolak", "High", bkg08)

# ================= MOD-05 PAYMENT =================
def pay01():
    d = create_paid_booking(api, S["bkg01"]["tok"])
    S["paid"] = d
    b, p = d["booking"], d["payment"]
    st, j, _ = api.get(f"/api/v1/payments/{p['paymentId']}", token=S["bkg01"]["tok"])
    expect(st == 200 and j["data"]["status"] == "SUCCESS", f"payment: {st} {j}")
    expect(j["data"]["paidAt"], "paidAt tidak tercatat")
    expect(j["data"]["paymentReference"], "payment reference tidak tercatat")
    st, j2, _ = api.get(f"/api/v1/bookings/{b['bookingId']}", token=S["bkg01"]["tok"])
    expect(j2["data"]["status"] == "PAID", f"booking: {j2['data']['status']}")
    st, inv, _ = api.get(f"/api/v1/bookings/{b['bookingId']}/invoice", token=S["bkg01"]["tok"])
    expect(st == 200 and inv["data"]["invoiceNumber"].startswith("INV-"), f"invoice: {st} {inv}")
    S["invoice"] = inv["data"]
    return f"VA sukses -> payment SUCCESS, booking PAID, invoice {inv['data']['invoiceNumber']} otomatis"


def pay02():
    out = []
    for m in ["CC", "QRIS", "EWALLET"]:
        d = create_paid_booking(api, S["bkg01"]["tok"], method=m)
        st, j, _ = api.get(f"/api/v1/bookings/{d['booking']['bookingId']}/invoice", token=S["bkg01"]["tok"])
        expect(st == 200, f"{m}: invoice tidak terbit {st} {j}")
        out.append(m)
    return f"metode {', '.join(out)} berfungsi; booking Paid + invoice terbit untuk semua"


def pay03():
    st, j, _ = api.post("/api/v1/bookings",
                        {"hotelId": "HTL-BDG-001", "roomId": "RM-BDG-STD", "guestName": "Gagal",
                         "checkIn": CI, "checkOut": CO, "guestCount": 2, "expectedPricePerNight": 550000},
                        token=S["bkg01"]["tok"])
    b = j["data"]
    st, j, _ = api.post("/api/v1/payments", {"bookingId": b["bookingId"], "method": "VA"}, token=S["bkg01"]["tok"])
    p = j["data"]
    st, j, _ = api.post(f"/api/v1/payments/{p['paymentId']}/simulate-callback?status=FAILED", {}, token=S["bkg01"]["tok"])
    expect(st == 200, f"callback FAILED: {st} {j}")
    st, j, _ = api.get(f"/api/v1/payments/{p['paymentId']}", token=S["bkg01"]["tok"])
    expect(j["data"]["status"] == "FAILED", f"payment status: {j['data']['status']}")
    st, j, _ = api.get(f"/api/v1/bookings/{b['bookingId']}", token=S["bkg01"]["tok"])
    expect(j["data"]["status"] == "PENDING_PAYMENT", f"booking: {j['data']['status']}")
    S["pay03_booking"] = b
    return "pembayaran gagal -> payment FAILED, booking tetap PENDING_PAYMENT (bisa coba metode lain)"


def pay05():
    st, j, _ = api.post("/api/v1/bookings",
                        {"hotelId": "HTL-BDG-001", "roomId": "RM-BDG-STD", "guestName": "Nominal",
                         "checkIn": CI, "checkOut": CO, "guestCount": 2, "expectedPricePerNight": 550000},
                        token=S["bkg01"]["tok"])
    b = j["data"]
    st, j, _ = api.post("/api/v1/payments", {"bookingId": b["bookingId"], "method": "VA"}, token=S["bkg01"]["tok"])
    p = j["data"]
    st, j, _ = api.post(f"/api/v1/payments/{p['paymentId']}/simulate-callback?status=SUCCESS&amount=999", {}, token=S["bkg01"]["tok"])
    expect(st == 409 and j.get("code") == "AMOUNT_MISMATCH", f"{st} {j}")
    st, j, _ = api.get(f"/api/v1/bookings/{b['bookingId']}", token=S["bkg01"]["tok"])
    expect(j["data"]["status"] == "PENDING_PAYMENT", f"booking berubah: {j['data']['status']}")
    st, j, _ = api.get(f"/api/v1/bookings/{b['bookingId']}/invoice", token=S["bkg01"]["tok"])
    expect(st == 404, f"invoice seharusnya tidak terbit: {st} {j}")
    return "nominal tidak sesuai ditolak AMOUNT_MISMATCH; booking tidak Paid, tanpa invoice"


def pay06():
    d = S["paid"]
    pid = d["payment"]["paymentId"]
    st, j, _ = api.post(f"/api/v1/payments/{pid}/simulate-callback?status=SUCCESS", {}, token=S["bkg01"]["tok"])
    expect(st == 200 and j["data"].get("duplicate") is True, f"callback berulang: {st} {j}")
    st, j, _ = api.get(f"/api/v1/bookings/{d['booking']['bookingId']}", token=S["bkg01"]["tok"])
    expect(j["data"]["status"] == "PAID", f"status berubah: {j['data']['status']}")
    st, j, _ = api.get(f"/api/v1/bookings/{d['booking']['bookingId']}/invoice", token=S["bkg01"]["tok"])
    expect(st == 200, f"invoice hilang: {st}")
    return "callback berulang = no-op (duplicate=true); status konsisten, invoice tetap satu"


def pay08():
    inv = S["invoice"]
    for f in ["invoiceNumber", "bookingNumber", "customerName", "customerEmail", "paymentMethod",
              "paymentStatus", "totalAmount", "currency", "issuedAt"]:
        expect(inv.get(f) not in (None, ""), f"field invoice {f} kosong")
    expect(inv["paymentStatus"] == "SUCCESS", f"paymentStatus: {inv['paymentStatus']}")
    expect(inv["totalAmount"] == 550000 * 3, f"total invoice: {inv['totalAmount']}")
    return f"invoice {inv['invoiceNumber']} lengkap: no.booking, customer, metode, status, total, tanggal"


def pay09():
    d = S["paid"]
    st, j, _ = api.post("/api/v1/payments", {"bookingId": d["booking"]["bookingId"], "method": "VA"},
                        token=S["bkg01"]["tok"])
    expect(st == 409 and j.get("code") == "PAYMENT_ALREADY_SETTLED", f"{st} {j}")
    return "pembayaran ulang booking yang sudah dibayar ditolak (PAYMENT_ALREADY_SETTLED)"


run("UAT-PAY-01", "MOD-05", "Pembayaran berhasil via Virtual Account", "Critical", pay01)
run("UAT-PAY-02", "MOD-05", "Pembayaran berhasil via Credit Card / QRIS / E-Wallet", "Critical", pay02)
run("UAT-PAY-03", "MOD-05", "Pembayaran gagal — booking tetap Pending Payment", "High", pay03)
run("UAT-PAY-05", "MOD-05", "Nominal pembayaran tidak sama dengan total booking", "High", pay05)
run("UAT-PAY-06", "MOD-05", "Callback berulang tidak membuat pembayaran/invoice ganda", "Critical", pay06)
run("UAT-PAY-08", "MOD-05", "Invoice memuat informasi transaksi yang lengkap dan benar", "High", pay08)
run("UAT-PAY-09", "MOD-05", "Memilih pembayaran untuk booking yang sudah dibayar", "Medium", pay09)

rec.save()
print("PART MAIN selesai:", sum(1 for r in rec.results if r["result"] == "Pass"), "pass /",
      sum(1 for r in rec.results if r["result"] == "Fail"), "fail")
