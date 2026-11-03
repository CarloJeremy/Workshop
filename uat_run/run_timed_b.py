"""UAT runner bagian 4 — server http://localhost:5211 (booking expiry 1 menit).
BKG-06 (booking expired + inventory lepas), PAY-04 (bayar booking expired), PAY-07 (callback terlambat)."""
import sys, os, time
from datetime import date, timedelta
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.modules.pop("uatlib", None)
from uatlib import Api, Recorder, uniq, register_verify_login

BASE = "http://localhost:5211"
api = Api(BASE)
rec = Recorder("timed_b")


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


S = {}
CI = (date.today() + timedelta(days=20)).isoformat()
CO = (date.today() + timedelta(days=22)).isoformat()


def setup():
    email = uniq("exp")
    tok, _ = register_verify_login(api, email)
    S["tok"] = tok
    st, j, _ = api.post("/api/v1/bookings",
                        {"hotelId": "HTL-JKT-001", "roomId": "RM-JKT-HNY", "guestName": "Expiry Test",
                         "checkIn": CI, "checkOut": CO, "guestCount": 2}, token=tok)
    expect(st == 200, f"booking: {st} {j}")
    S["booking"] = j["data"]
    st, j, _ = api.post("/api/v1/payments", {"bookingId": S["booking"]["bookingId"], "method": "VA"}, token=tok)
    expect(st == 200, f"payment: {st} {j}")
    S["payment"] = j["data"]
    return "ok"


setup()
print("  menunggu ~75 detik hingga booking melewati batas 30 menit (dipercepat jadi 1 menit)...")
time.sleep(75)


def bkg06():
    st, j, _ = api.get(f"/api/v1/bookings/{S['booking']['bookingId']}", token=S["tok"])
    expect(st == 200 and j["data"]["status"] == "EXPIRED",
           f"booking belum expired: {st} {j['data']['status']}")
    expect(j["data"]["expiredAt"], "expiredAt tidak tercatat")
    S["expired"] = j["data"]
    # inventory dilepaskan: kamar 1 unit kembali muncul tersedia
    st, j, _ = api.get(f"/api/v1/hotels/search?destination=Jakarta&checkIn={CI}&checkOut={CO}&guestCount=2")
    rooms = [r for h in j["data"] for r in h["rooms"] if r["roomId"] == "RM-JKT-HNY"]
    expect(rooms and rooms[0]["availableRooms"] >= 1, f"kamar belum dilepas: {rooms}")
    return "booking otomatis EXPIRED setelah batas (terdeteksi <= 1 menit); kamar kembali tersedia"


def pay04():
    st, j, _ = api.post("/api/v1/payments", {"bookingId": S["booking"]["bookingId"], "method": "VA"}, token=S["tok"])
    expect(st == 422 and j.get("code") == "BOOKING_EXPIRED", f"{st} {j}")
    return "pembayaran untuk booking Expired ditolak (BOOKING_EXPIRED)"


def pay07():
    st, j, _ = api.post(f"/api/v1/payments/{S['payment']['paymentId']}/simulate-callback?status=SUCCESS", {},
                        token=S["tok"])
    expect(st == 200 and j["data"].get("late_callback") is True, f"{st} {j}")
    st, j, _ = api.get(f"/api/v1/bookings/{S['booking']['bookingId']}", token=S["tok"])
    expect(j["data"]["status"] == "EXPIRED", f"status berubah: {j['data']['status']}")
    st, j, _ = api.get(f"/api/v1/bookings/{S['booking']['bookingId']}/invoice", token=S["tok"])
    expect(st == 404, f"invoice tidak sah terbit: {st} {j}")
    return "callback terlambat setelah Expired: status TIDAK berubah, tanpa invoice/e-ticket, dicatat utk rekonsiliasi"


run("UAT-BKG-06", "MOD-04", "Booking expired setelah 30 menit tanpa pembayaran", "Critical", bkg06)
run("UAT-PAY-04", "MOD-05", "Pembayaran ditolak untuk booking expired", "Critical", pay04)
run("UAT-PAY-07", "MOD-05", "Callback terlambat setelah booking expired/cancelled", "High", pay07)

rec.save()
print("PART TIMED-B selesai:", sum(1 for r in rec.results if r["result"] == "Pass"), "pass /",
      sum(1 for r in rec.results if r["result"] == "Fail"), "fail")
