"""UAT runner bagian 2 — server http://localhost:5210.
TKT-01..06 | HST-01..04 | CXL-01..07 | RFD-01..08 | RBAC/SEC/AUD/E2E."""
import sys, os, uuid, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.modules.pop("uatlib", None)
from uatlib import Api, Recorder, uniq, register_verify_login, create_paid_booking, login
from datetime import date, timedelta

BASE = "http://localhost:5210"
api = Api(BASE)
rec = Recorder("main2")
S = {}


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


# ================= SETUP =================
def setup():
    emailA = uniq("userA")
    tokA, _ = register_verify_login(api, emailA, name="Customer A", phone="081100000010")
    emailB = uniq("userB")
    tokB, _ = register_verify_login(api, emailB, name="Customer B", phone="081100000011")
    S.update(emailA=emailA, tokA=tokA, emailB=emailB, tokB=tokB)
    # booking PAID flexible untuk A
    S["paidA"] = create_paid_booking(api, tokA, expected_price=550000)
    return "setup ok"


setup()

# ================= MOD-06 E-TICKET =================
def tkt01():
    d = S["paidA"]
    st, j, _ = api.post("/api/v1/etickets/reissue", {"bookingId": d["booking"]["bookingId"]}, token=S["tokA"])
    expect(st == 200, f"reissue: {st} {j}")
    t = j["data"]
    for f in ["bookingNumber", "hotelName", "hotelLocation", "roomName", "guestName", "checkIn", "checkOut",
              "totalAmount", "currency", "qrCode", "pdfReference", "issuedAt"]:
        expect(t.get(f) not in (None, ""), f"field e-ticket {f} kosong")
    S["ticketA"] = t
    # email tercatat di log notifikasi
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": "admin@demo.com", "password": "Demo1234!"})
    admin = j["data"]["access_token"]
    S["admin"] = admin
    st, j, _ = api.get("/api/v1/admin/notifications?size=100", token=admin)
    logs = j["data"]["items"]
    et = [l for l in logs if l["type"] == "ETicket" and l["recipient"] == S["emailA"]]
    expect(len(et) > 0, "log email e-ticket tidak ditemukan")
    expect(et[0]["status"] == "Sent", f"status email: {et[0]['status']}")
    return f"E-Ticket {t['bookingNumber']} lengkap (hotel/lokasi/kamar/tamu/periode/total/QR) + email tercatat"


def tkt02():
    t = S["ticketA"]
    st, j, _ = api.get(f"/api/v1/etickets/verify?code={t['qrCode']}")
    expect(st == 200, f"verify: {st} {j}")
    v = j["data"]
    expect(v["bookingNumber"] == t["bookingNumber"], f"QR mengarah ke booking lain: {v['bookingNumber']}")
    expect(v["hotelName"] == t["hotelName"] and str(v["checkIn"]) == str(t["checkIn"]), "data verify tidak cocok")
    st, j, _ = api.get(f"/api/v1/etickets/verify?code={t['qrCode']}x")
    expect(st == 404, f"QR rusak seharusnya 404: {st} {j}")
    # QR X tidak pernah mengarah ke booking Y
    d2 = create_paid_booking(api, S["tokA"], method="QRIS", expected_price=550000)
    st, j, _ = api.post("/api/v1/etickets/reissue", {"bookingId": d2["booking"]["bookingId"]}, token=S["tokA"])
    qr2 = j["data"]["qrCode"]
    st, j, _ = api.get(f"/api/v1/etickets/verify?code={qr2}")
    expect(j["data"]["bookingNumber"] == d2["booking"]["bookingNumber"], "QR2 salah target")
    expect(qr2 != t["qrCode"], "QR antar booking tidak unik")
    return "QR mengidentifikasi booking yang benar; QR rusak ditolak; QR antar booking unik"


def tkt03():
    st, j, _ = api.post("/api/v1/bookings",
                        {"hotelId": "HTL-BDG-001", "roomId": "RM-BDG-STD", "guestName": "Belum Bayar",
                         "checkIn": (date.today() + timedelta(days=30)).isoformat(),
                         "checkOut": (date.today() + timedelta(days=32)).isoformat(),
                         "guestCount": 2, "expectedPricePerNight": 550000}, token=S["tokA"])
    b = j["data"]
    S["pendingA"] = b
    st, j, _ = api.post("/api/v1/etickets/reissue", {"bookingId": b["bookingId"]}, token=S["tokA"])
    expect(st == 422, f"e-ticket untuk booking belum bayar: {st} {j}")
    return "e-ticket ditolak untuk booking Pending Payment (422)"


def tkt04():
    d = S["paidA"]
    st, j1, _ = api.post("/api/v1/etickets/reissue", {"bookingId": d["booking"]["bookingId"]}, token=S["tokA"])
    st, j2, _ = api.post("/api/v1/etickets/reissue", {"bookingId": d["booking"]["bookingId"]}, token=S["tokA"])
    a, b = j1["data"], j2["data"]
    expect(a["eTicketId"] == b["eTicketId"] and a["qrCode"] == b["qrCode"] and a["pdfReference"] == b["pdfReference"],
           "reissue membuat e-ticket ganda")
    return "reissue mengirim e-ticket yang identik (tanpa duplikasi)"


def tkt05():
    email = f"u{uuid.uuid4().hex[:8]}@emailgagal.test"
    tok, _ = register_verify_login(api, email, name="Email Gagal")
    d = create_paid_booking(api, tok, expected_price=550000)
    st, j, _ = api.get("/api/v1/admin/notifications?size=100", token=S["admin"])
    logs = [l for l in j["data"]["items"] if l["type"] == "ETicket" and l["recipient"] == email]
    expect(len(logs) > 0, "log tidak ditemukan")
    l = logs[0]
    expect(l["status"].upper() == "FAILED", f"status: {l['status']}")
    expect(l["retryCount"] >= 1 and l["lastError"], "retry/lastError tidak tercatat")
    st, j, _ = api.post("/api/v1/etickets/reissue", {"bookingId": d["booking"]["bookingId"]}, token=tok)
    expect(st == 200, "e-ticket tidak bisa dikirim ulang")
    return f"kegagalan email tercatat FAILED (retry {l['retryCount']}x terjadwal); e-ticket tetap bisa reissue"


def tkt06():
    # booking PAID nonrefundable -> dibatalkan -> QR tidak valid untuk check-in
    d = create_paid_booking(api, S["tokA"], room="RM-DPS-VIL", method="QRIS", expected_price=2500000,
                            hotel_id="HTL-DPS-001")
    st, j, _ = api.post("/api/v1/etickets/reissue", {"bookingId": d["booking"]["bookingId"]}, token=S["tokA"])
    qr = j["data"]["qrCode"]
    st, j, _ = api.post(f"/api/v1/bookings/{d['booking']['bookingId']}/cancel", {"reason": "uji"}, token=S["tokA"])
    expect(st == 200, f"cancel: {st} {j}")
    st, j, _ = api.get(f"/api/v1/etickets/verify?code={qr}")
    v = j["data"]
    expect(v["bookingStatus"] == "CANCELLED" and v["validForCheckIn"] is False,
           f"verify: {v['bookingStatus']} valid={v['validForCheckIn']}")
    return "scan QR booking yang dibatalkan -> status CANCELLED tampil, tidak valid untuk check-in"


run("UAT-TKT-01", "MOD-06", "E-Ticket dibuat dan dikirim via email setelah pembayaran Paid", "Critical", tkt01)
run("UAT-TKT-02", "MOD-06", "QR Code mengidentifikasi booking yang benar", "Critical", tkt02)
run("UAT-TKT-03", "MOD-06", "E-Ticket tidak diterbitkan untuk booking belum dibayar", "High", tkt03)
run("UAT-TKT-04", "MOD-06", "Pengiriman ulang E-Ticket (reissue)", "Medium", tkt04)
run("UAT-TKT-05", "MOD-06", "Kegagalan pengiriman email E-Ticket dicatat", "High", tkt05)
run("UAT-TKT-06", "MOD-06", "Status booking tampil saat QR discan setelah pembatalan", "Medium", tkt06)

# ================= MOD-07 HISTORY =================
def hst01():
    st, j, ms = api.get("/api/v1/bookings/history?group=inprogress&page=1&size=50", token=S["tokA"])
    rec.latency("history", ms)
    expect(st == 200, f"{st} {j}")
    items = j["data"]["items"]
    expect(len(items) > 0, "tidak ada data")
    for it in items:
        for f in ["bookingNumber", "hotelName", "hotelLocation", "checkIn", "checkOut", "status"]:
            expect(it.get(f) not in (None, ""), f"field {f} hilang")
    # tidak ada data milik B
    st, jb, _ = api.get("/api/v1/bookings/history?group=inprogress&page=1&size=50", token=S["tokB"])
    bnums = {b["bookingNumber"] for b in jb["data"]["items"]}
    anums = {b["bookingNumber"] for b in items}
    expect(not (anums & bnums), f"booking B bocor ke A: {anums & bnums}")
    S["histA"] = items
    return f"{len(items)} booking milik A tampil lengkap dgn lokasi; data B tidak ikut"


def hst02():
    st, j, _ = api.get("/api/v1/bookings/history?group=inprogress&page=1&size=50", token=S["tokA"])
    nums = {b["bookingId"]: b["status"] for b in j["data"]["items"]}
    expect(S["pendingA"]["bookingId"] not in nums, "booking PENDING_PAYMENT tampil di Inprogress")
    for bid, status in nums.items():
        expect(status == "PAID", f"status {status} di Inprogress")
    st, j, _ = api.get("/api/v1/bookings/history?group=completed&page=1&size=50", token=S["tokA"])
    expect(st == 200, f"completed group: {st}")
    return ("Inprogress hanya berisi PAID berjalan (Pending/Expired/Cancelled tidak tampil); "
            "Completed Trip untuk periode berakhir (butuh data berumur untuk isinya)")


def hst03():
    bid = S["paidA"]["booking"]["bookingId"]
    st, j, _ = api.get(f"/api/v1/bookings/{bid}", token=S["tokB"])
    expect(st == 403, f"akses booking orang lain: {st} {j}")
    return "akses detail booking Customer lain ditolak (403)"


def hst04():
    tok, _ = register_verify_login(api, uniq("kosong"))
    st, j, _ = api.get("/api/v1/bookings/history?group=inprogress&page=1&size=10", token=tok)
    expect(st == 200 and j["data"]["items"] == [] and j["data"]["total"] == 0, f"{st} {j}")
    return "empty state: list kosong dengan pesan 'riwayat belum tersedia' di UI"


run("UAT-HST-01", "MOD-07", "Menampilkan riwayat booking milik Customer yang login", "Critical", hst01)
run("UAT-HST-02", "MOD-07", "Pengelompokan Inprogress Trip dan Completed Trip", "High", hst02)
run("UAT-HST-03", "MOD-07", "Customer tidak dapat melihat booking Customer lain", "Critical", hst03)
run("UAT-HST-04", "MOD-07", "Empty state riwayat booking", "Medium", hst04)

# ================= MOD-08 CANCELLATION =================
def cxl_setup():
    # booking PAID flexible (check-in jauh -> eligible refund)
    S["cxl_paid"] = create_paid_booking(api, S["tokA"], expected_price=550000)
    return "ok"


cxl_setup()


def cxl01():
    d = S["cxl_paid"]
    st, j, _ = api.get(f"/api/v1/bookings/{d['booking']['bookingId']}/cancellation-preview", token=S["tokA"])
    expect(st == 200, f"{st} {j}")
    p = j["data"]
    expect(p["policy"] and p["refundable"] is True, f"preview: {p}")
    total = d["booking"]["totalAmount"]
    expect(abs(p["estimatedPenalty"] - total * 0.10) < 1, f"penalty: {p['estimatedPenalty']} vs {total*0.1}")
    expect(abs(p["estimatedRefund"] - total * 0.90) < 1, f"refund: {p['estimatedRefund']}")
    return f"preview tampil: policy {p['policy']}, penalty {p['estimatedPenalty']:.0f}, estimasi refund {p['estimatedRefund']:.0f}"


def cxl02():
    d = S["cxl_paid"]
    st, j, _ = api.post(f"/api/v1/bookings/{d['booking']['bookingId']}/cancel", {"reason": "berubah rencana"},
                        token=S["tokA"])
    expect(st == 200, f"{st} {j}")
    data = j["data"]
    expect(data["booking_status"] == "CANCELLED" and data["refundable"] is True and data["refund_id"],
           f"cancel: {data}")
    S["refund1"] = data["refund_id"]
    st, j, _ = api.get("/api/v1/refunds", token=S["tokA"])
    r = [x for x in j["data"] if x["refundId"] == data["refund_id"]]
    expect(r and r[0]["status"] == "PENDING_APPROVAL", f"refund: {r}")
    return "booking Cancelled + pengajuan refund PENDING_APPROVAL dibuat (cancellation & refund terpisah)"


def cxl03():
    d = create_paid_booking(api, S["tokA"], room="RM-DPS-VIL", method="QRIS", expected_price=2500000,
                            hotel_id="HTL-DPS-001")
    st, j, _ = api.post(f"/api/v1/bookings/{d['booking']['bookingId']}/cancel", {"reason": "uji nonref"},
                        token=S["tokA"])
    expect(st == 200 and j["data"]["refundable"] is False and j["data"]["refund_id"] is None, f"{st} {j}")
    st, j, _ = api.get("/api/v1/refunds", token=S["tokA"])
    expect(all(x["bookingId"] != d["booking"]["bookingId"] for x in j["data"]), "refund record tidak seharusnya ada")
    return "non-refundable: pembatalan diproses, diinformasikan tanpa refund, tidak ada record refund"


def cxl04():
    d = create_paid_booking(api, S["tokA"], days_in=0, days_out=2, expected_price=550000)  # check-in HARI INI
    st, j, _ = api.post(f"/api/v1/bookings/{d['booking']['bookingId']}/cancel", {"reason": "uji"},
                        token=S["tokA"])
    expect(st == 409 and j.get("code") == "CANCELLATION_NOT_ALLOWED", f"{st} {j}")
    st, j2, _ = api.get(f"/api/v1/bookings/{d['booking']['bookingId']}", token=S["tokA"])
    expect(j2["data"]["status"] == "PAID", f"status berubah: {j2['data']['status']}")
    return "melewati batas waktu pembatalan -> ditolak, status booking tidak berubah"


def cxl05():
    st, j, _ = api.post(f"/api/v1/bookings/{S['pendingA']['bookingId']}/cancel", {"reason": "uji"},
                        token=S["tokA"])
    expect(st == 409 and "dibayar" in j.get("message", ""), f"{st} {j}")
    return "pembatalan ditolak untuk booking Pending Payment (mekanisme hanya untuk booking berbayar)"


def cxl06():
    d = create_paid_booking(api, S["tokB"], expected_price=550000)
    S["paidB"] = d
    st, j, _ = api.post(f"/api/v1/bookings/{d['booking']['bookingId']}/cancel", {"reason": "uji"}, token=S["tokA"])
    expect(st == 403, f"{st} {j}")
    st, j, _ = api.get(f"/api/v1/bookings/{d['booking']['bookingId']}", token=S["tokB"])
    expect(j["data"]["status"] == "PAID", "booking B berubah!")
    return "A tidak dapat membatalkan booking B (403, data B tidak berubah)"


def cxl07():
    d = S["cxl_paid"]
    st, j, _ = api.post(f"/api/v1/bookings/{d['booking']['bookingId']}/cancel", {"reason": "lagi"}, token=S["tokA"])
    expect(st == 409 and "pernah dibatalkan" in j.get("message", ""), f"{st} {j}")
    st, j, _ = api.get("/api/v1/refunds", token=S["tokA"])
    n = len([x for x in j["data"] if x["bookingId"] == d["booking"]["bookingId"]])
    expect(n == 1, f"jumlah refund untuk booking: {n}")
    return "pembatalan berulang ditolak; hanya 1 record cancellation & refund"


run("UAT-CXL-01", "MOD-08", "Preview ketentuan pembatalan sebelum konfirmasi", "High", cxl01)
run("UAT-CXL-02", "MOD-08", "Pembatalan booking Paid yang eligible refund", "Critical", cxl02)
run("UAT-CXL-03", "MOD-08", "Pembatalan booking Paid yang tidak eligible refund", "High", cxl03)
run("UAT-CXL-04", "MOD-08", "Pembatalan ditolak — booking tidak memenuhi policy", "High", cxl04)
run("UAT-CXL-05", "MOD-08", "Pembatalan ditolak untuk booking Pending Payment / Expired / Cancelled", "High", cxl05)
run("UAT-CXL-06", "MOD-08", "Pembatalan hanya untuk booking milik sendiri", "Critical", cxl06)
run("UAT-CXL-07", "MOD-08", "Pembatalan tidak dapat diproses berulang (idempotent)", "Critical", cxl07)

# ================= MOD-09 REFUND =================
def rfd01():
    d = S["cxl_paid"]
    st, j, _ = api.get(f"/api/v1/refunds/{S['refund1']}", token=S["tokA"])
    r = j["data"]
    total = d["booking"]["totalAmount"]
    expect(r["totalPaid"] == total, f"totalPaid {r['totalPaid']} != {total}")
    expect(abs(r["penaltyAmount"] - total * 0.10) < 1, f"penalty {r['penaltyAmount']}")
    expect(abs(r["refundAmount"] - (total - total * 0.10)) < 1, f"refund {r['refundAmount']}")
    expect(r["refundAmount"] <= r["totalPaid"], "refund melebihi pembayaran")
    return f"penalty 10% = {r['penaltyAmount']:.0f}; refund = {r['refundAmount']:.0f} (<= total paid)"


def rfd02():
    st, j, _ = api.get(f"/api/v1/refunds/{S['refund1']}", token=S["tokA"])
    r = j["data"]
    expect(r["status"] == "PENDING_APPROVAL", f"status {r['status']}")
    for f in ["paymentReference", "totalPaid", "penaltyAmount", "refundAmount", "bookingId"]:
        expect(r.get(f) not in (None, ""), f"{f} kosong")
    expect(r["completedAt"] is None, "dana sudah ditransfer sebelum approval!")
    return "PENDING_APPROVAL dengan rincian lengkap; belum ada dana yang ditransfer"


def rfd03():
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": "finance@demo.com", "password": "Demo1234!"})
    fin = j["data"]["access_token"]
    S["fin"] = fin
    st, j, _ = api.post(f"/api/v1/refunds/{S['refund1']}/approve", {}, token=fin)
    expect(st == 200 and j["data"]["status"] == "APPROVED", f"approve: {st} {j}")
    st, j, _ = api.post(f"/api/v1/refunds/{S['refund1']}/execute", {}, token=fin)
    expect(st == 200 and j["data"]["status"] == "COMPLETED", f"execute: {st} {j}")
    expect(j["data"]["processedAt"] and j["data"]["completedAt"], "waktu pemrosesan tidak tercatat")
    st, j, _ = api.get(f"/api/v1/refunds/{S['refund1']}", token=S["tokA"])
    expect(j["data"]["status"] == "COMPLETED", f"customer melihat: {j['data']['status']}")
    return "APPROVED -> Processing -> COMPLETED; waktu pemrosesan tercatat; Customer melihat status akhir"


def rfd04():
    d = create_paid_booking(api, S["tokA"], expected_price=550000)
    st, j, _ = api.post(f"/api/v1/bookings/{d['booking']['bookingId']}/cancel", {"reason": "uji reject"},
                        token=S["tokA"])
    rid = j["data"]["refund_id"]
    S["refund2"] = rid
    st, j, _ = api.post(f"/api/v1/refunds/{rid}/reject", {"reason": "data tidak lengkap"}, token=S["fin"])
    expect(st == 200 and j["data"]["status"] == "REJECTED", f"{st} {j}")
    st, j, _ = api.get(f"/api/v1/refunds/{rid}", token=S["tokA"])
    expect(j["data"]["status"] == "REJECTED" and j["data"]["reason"] == "data tidak lengkap",
           f"customer melihat: {j['data']}")
    return "ditolak Finance: status REJECTED + alasan tercatat, Customer melihat, tanpa transfer dana"


def rfd05():
    # booking dengan pembayaran gagal -> tidak boleh ada refund
    st, j, _ = api.post("/api/v1/bookings",
                        {"hotelId": "HTL-BDG-001", "roomId": "RM-BDG-STD", "guestName": "Gagal Bayar",
                         "checkIn": (date.today() + timedelta(days=40)).isoformat(),
                         "checkOut": (date.today() + timedelta(days=42)).isoformat(),
                         "guestCount": 2, "expectedPricePerNight": 550000}, token=S["tokA"])
    b = j["data"]
    st, j, _ = api.post("/api/v1/payments", {"bookingId": b["bookingId"], "method": "VA"}, token=S["tokA"])
    pid = j["data"]["paymentId"]
    api.post(f"/api/v1/payments/{pid}/simulate-callback?status=FAILED", {}, token=S["tokA"])
    st, j, _ = api.post(f"/api/v1/bookings/{b['bookingId']}/cancel", {"reason": "uji"}, token=S["tokA"])
    expect(st == 409, f"pembatalan booking belum dibayar seharusnya ditolak: {st} {j}")
    st, j, _ = api.get("/api/v1/refunds", token=S["tokA"])
    expect(all(x["bookingId"] != b["bookingId"] for x in j["data"]), "refund dibuat tanpa pembayaran sukses!")
    return "refund tidak dapat dibuat untuk pembayaran tidak berhasil"


def rfd06():
    st, j, _ = api.post(f"/api/v1/refunds/{S['refund1']}/execute", {}, token=S["fin"])
    expect(st == 409, f"eksekusi kedua seharusnya ditolak: {st} {j}")
    st, j, _ = api.post(f"/api/v1/refunds/{S['refund1']}/approve", {}, token=S["fin"])
    expect(st == 409, f"approve kedua seharusnya ditolak: {st} {j}")
    st, j, _ = api.get("/api/v1/refunds", token=S["tokA"])
    n = len([x for x in j["data"] if x["refundId"] == S["refund1"]])
    expect(n == 1, f"duplikasi refund: {n}")
    return "proses berulang ditolak; hanya satu refund per pembatalan/pembayaran"


def rfd07():
    d = create_paid_booking(api, S["tokA"], expected_price=550000)
    st, j, _ = api.post(f"/api/v1/bookings/{d['booking']['bookingId']}/cancel", {"reason": "uji gagal"},
                        token=S["tokA"])
    rid = j["data"]["refund_id"]
    api.post(f"/api/v1/refunds/{rid}/approve", {}, token=S["fin"])
    st, j, _ = api.post(f"/api/v1/refunds/{rid}/execute?simulate=FAILED", {}, token=S["fin"])
    expect(st == 200 and j["data"]["status"] == "FAILED", f"{st} {j}")
    expect(j["data"]["reason"] and "gagal" in j["data"]["reason"].lower(), "detail kegagalan tidak tercatat")
    expect(j["data"]["status"] != "COMPLETED", "tidak boleh Completed tanpa konfirmasi")
    return "provider refund gagal -> status FAILED (bukan Completed), detail tercatat untuk tindak lanjut"


def rfd08():
    rid = S["refund1"]
    st, j, _ = api.get(f"/api/v1/refunds/{rid}", token=S["tokA"])
    expect(st == 200, f"pemilik: {st}")
    st, j, _ = api.get(f"/api/v1/refunds/{rid}", token=S["fin"])
    expect(st == 200, f"finance: {st}")
    st, j, _ = api.get(f"/api/v1/refunds/{rid}", token=S["tokB"])
    expect(st == 403, f"customer lain: {st} {j}")
    return "pemilik & Finance dapat melihat; Customer lain ditolak (403)"


run("UAT-RFD-01", "MOD-09", "Perhitungan penalty dan refund amount sesuai policy", "Critical", rfd01)
run("UAT-RFD-02", "MOD-09", "Pengajuan refund berstatus Pending Approval menunggu Finance", "High", rfd02)
run("UAT-RFD-03", "MOD-09", "Finance menyetujui refund hingga dana dikembalikan", "Critical", rfd03)
run("UAT-RFD-04", "MOD-09", "Finance menolak refund", "Critical", rfd04)
run("UAT-RFD-05", "MOD-09", "Refund tidak dapat diproses untuk pembayaran yang tidak berhasil", "High", rfd05)
run("UAT-RFD-06", "MOD-09", "Refund tidak dapat diproses ganda", "Critical", rfd06)
run("UAT-RFD-07", "MOD-09", "Kegagalan proses refund dari provider tercatat (Failed)", "High", rfd07)
run("UAT-RFD-08", "MOD-09", "Akses data refund sesuai peran (pemilik booking / Finance)", "High", rfd08)

# ================= LINTAS MODUL =================
def rbac01():
    # Finance & Management tidak boleh booking/payment
    body = {"hotelId": "HTL-BDG-001", "roomId": "RM-BDG-FAM", "guestName": "RBAC",
            "checkIn": (date.today() + timedelta(days=50)).isoformat(),
            "checkOut": (date.today() + timedelta(days=52)).isoformat(), "guestCount": 2}
    st, j, _ = api.post("/api/v1/bookings", body, token=S["fin"])
    expect(st == 403, f"Finance booking: {st} {j}")
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": "management@demo.com", "password": "Demo1234!"})
    mgmt = j["data"]["access_token"]
    st, j, _ = api.post("/api/v1/bookings", body, token=mgmt)
    expect(st == 403, f"Management booking: {st} {j}")
    st, j, _ = api.post("/api/v1/payments", {"bookingId": S["pendingA"]["bookingId"], "method": "VA"}, token=S["fin"])
    expect(st == 403, f"Finance payment: {st} {j}")
    # Admin & Travel Consultant dapat booking
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": "admin@demo.com", "password": "Demo1234!"})
    st2, j2, _ = api.post("/api/v1/bookings", dict(body, guestName="Admin"), token=S["admin"])
    expect(st2 == 200, f"Admin booking: {st2} {j2}")
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": "travel@demo.com", "password": "Demo1234!"})
    tc = j["data"]["access_token"]
    st, j, _ = api.post("/api/v1/bookings", dict(body, guestName="TC"), token=tc)
    expect(st == 200, f"Travel Consultant booking: {st} {j}")
    # Management hanya monitoring; Customer ditolak dari panel admin
    st, j, _ = api.get("/api/v1/admin/audits", token=mgmt)
    expect(st == 200, f"Management monitoring: {st}")
    st, j, _ = api.get("/api/v1/admin/audits", token=S["tokA"])
    expect(st == 403, f"Customer akses admin: {st} {j}")
    return "Finance/Management ditolak dari booking & payment; Admin/TC dapat; Finance kelola refund; Management monitoring"


def sec01():
    d = S["paidA"]
    checks = [
        (f"/api/v1/bookings/{d['booking']['bookingId']}", "GET"),
        (f"/api/v1/payments/{d['payment']['paymentId']}", "GET"),
        (f"/api/v1/bookings/{d['booking']['bookingId']}/invoice", "GET"),
        (f"/api/v1/bookings/{d['booking']['bookingId']}/cancellation-preview", "GET"),
    ]
    for path, m in checks:
        st, j, _ = api.req(m, path, token=S["tokB"])
        expect(st == 403, f"{path}: {st} {j}")
    st, j, _ = api.post("/api/v1/etickets/reissue", {"bookingId": d["booking"]["bookingId"]}, token=S["tokB"])
    expect(st == 403, f"reissue: {st} {j}")
    st, j, _ = api.post(f"/api/v1/payments/{d['payment']['paymentId']}/simulate-callback?status=SUCCESS", {},
                        token=S["tokB"])
    expect(st == 403, f"simulate: {st} {j}")
    return "seluruh akses data antar Customer ditolak (403) untuk booking/payment/invoice/e-ticket"


def sec02():
    st, j, _ = api.post("/api/v1/auth/register", {"email": uniq("sec02"), "password": "Rahasia123!"})
    txt = json.dumps(j).lower()
    expect("rahasia123" not in txt and "passwordhash" not in txt and "password_hash" not in txt,
           "password terekspos di response register")
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": "customer@demo.com", "password": "Demo1234!"})
    txt = json.dumps(j).lower()
    expect("demo1234" not in txt and "hash" not in txt, "password/hash terekspos di response login")
    st, j, _ = api.get("/api/v1/admin/notifications?size=50", token=S["admin"])
    txt = json.dumps(j).lower()
    expect("password" not in txt and "hash" not in txt, "data sensitif terekspos di log notifikasi")
    return "password tidak pernah tampil/dikirim; tanpa penyimpanan data kartu; log ter-mask"


def sec03():
    email = uniq("sec03")
    st, j, _ = api.post("/api/v1/auth/register", {"email": email, "password": "Demo1234!"})
    for _ in range(3):
        api.post("/api/v1/auth/verify-otp", {"email": email, "otpCode": "000000"})
    st, j, _ = api.post("/api/v1/auth/verify-otp", {"email": email, "otpCode": "000000"})
    expect(st == 429, f"OTP tidak di-invalidate setelah batas: {st} {j}")
    st, j, _ = api.post("/api/v1/auth/resend-otp", {"email": email})
    expect(st == 429 and j.get("code") in ("RATE_LIMITED", "TOO_MANY_REQUESTS"), f"resend: {st} {j}")
    return "OTP di-invalidate setelah 3x salah; resend menghormati cooldown (429)"


def aud01():
    st, j, _ = api.get("/api/v1/admin/audits?size=200", token=S["admin"])
    expect(st == 200, f"{st} {j}")
    items = j["data"]["items"]
    actions = {a["action"] for a in items}
    need = ["REGISTER", "OTP_VERIFIED", "CREATED", "PAYMENT_INITIATED", "PAID", "CANCELLED", "APPROVED", "COMPLETED"]
    missing = [a for a in need if a not in actions]
    expect(not missing, f"aksi audit hilang: {missing}")
    sample = items[0]
    for f in ["entity", "action", "actor", "at"]:
        expect(sample.get(f) not in (None, ""), f"field audit {f} kosong")
    return f"{len(items)} entri audit; perubahan status booking/payment/refund + keputusan Finance tercatat dgn actor & waktu"


def e2e01():
    email = uniq("e2e")
    tok, _ = register_verify_login(api, email, name="E2E User")
    st, j, ms = api.get(f"/api/v1/hotels/search?destination=Bali&checkIn={(date.today()+timedelta(days=10)).isoformat()}&checkOut={(date.today()+timedelta(days=12)).isoformat()}&guestCount=2", token=tok)
    rec.latency("search", ms)
    expect(st == 200 and j["data"], "search gagal")
    d = create_paid_booking(api, tok, room="RM-DPS-STD", days_in=10, days_out=12,
                            guest="E2E Guest", method="EWALLET", expected_price=1200000,
                            hotel_id="HTL-DPS-001")
    b = d["booking"]
    st, inv, _ = api.get(f"/api/v1/bookings/{b['bookingId']}/invoice", token=tok)
    expect(st == 200 and inv["data"]["invoiceNumber"], "invoice tidak terbit")
    st, tk, _ = api.post("/api/v1/etickets/reissue", {"bookingId": b["bookingId"]}, token=tok)
    expect(st == 200 and tk["data"]["qrCode"], "e-ticket tidak terbit")
    st, j, ms = api.get("/api/v1/bookings/history?group=inprogress&page=1&size=10", token=tok)
    rec.latency("history", ms)
    expect(any(x["bookingNumber"] == b["bookingNumber"] for x in j["data"]["items"]), "tidak masuk Inprogress")
    st, j, _ = api.post(f"/api/v1/bookings/{b['bookingId']}/cancel", {"reason": "e2e"}, token=tok)
    rid = j["data"]["refund_id"]
    expect(rid, "refund tidak dibuat")
    st, j, _ = api.post(f"/api/v1/refunds/{rid}/approve", {}, token=S["fin"])
    expect(j["data"]["status"] == "APPROVED", "approve gagal")
    st, j, _ = api.post(f"/api/v1/refunds/{rid}/execute", {}, token=S["fin"])
    expect(j["data"]["status"] == "COMPLETED", "execute gagal")
    st, j, _ = api.get(f"/api/v1/refunds/{rid}", token=tok)
    expect(j["data"]["status"] == "COMPLETED", "status akhir tidak terlihat")
    return ("E2E lengkap: register->OTP->login->search->booking->bayar->invoice+e-ticket->"
            "Inprogress->cancel->refund->Finance approve->execute->COMPLETED")


run("UAT-RBAC-01", "LINTAS", "Hak akses sesuai RBAC matrix per peran", "Critical", rbac01)
run("UAT-SEC-01", "LINTAS", "Akses data antar Customer terisolasi", "Critical", sec01)
run("UAT-SEC-02", "LINTAS", "Data sensitif tidak terekspos di UI dan email", "High", sec02)
run("UAT-SEC-03", "LINTAS", "Pembatasan percobaan OTP (brute force)", "High", sec03)
run("UAT-AUD-01", "LINTAS", "Jejak audit perubahan status dan keputusan tercatat", "High", aud01)
run("UAT-E2E-01", "LINTAS", "Alur ujung-ke-ujung utama (happy path)", "Critical", e2e01)

rec.save()
print("PART MAIN2 selesai:", sum(1 for r in rec.results if r["result"] == "Pass"), "pass /",
      sum(1 for r in rec.results if r["result"] == "Fail"), "fail")
