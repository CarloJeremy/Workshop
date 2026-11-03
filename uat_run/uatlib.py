"""Helper UAT runner — Hotel Booking Prototype.
Mengeksekusi skenario UAT terhadap API nyata dan menyimpan hasil ke JSON."""
import json, urllib.request, urllib.error, time, uuid, re, os

HERE = os.path.dirname(os.path.abspath(__file__))

class Api:
    def __init__(self, base):
        self.base = base

    def req(self, method, path, body=None, token=None, raw_body=None, headers=None, form=False):
        url = self.base + path
        data = None
        hdrs = {} if form else {"Content-Type": "application/json"}
        if token:
            hdrs["Authorization"] = "Bearer " + token
        if headers:
            hdrs.update(headers)
        if body is not None:
            data = json.dumps(body).encode()
        if raw_body is not None:
            data = raw_body.encode() if isinstance(raw_body, str) else raw_body
        r = urllib.request.Request(url, data=data, headers=hdrs, method=method)
        t0 = time.time()
        try:
            with urllib.request.urlopen(r, timeout=20) as resp:
                txt = resp.read().decode()
                ms = (time.time() - t0) * 1000
                try:
                    return resp.status, (json.loads(txt) if txt else {}), ms
                except Exception:
                    return resp.status, {"raw": txt}, ms
        except urllib.error.HTTPError as e:
            txt = e.read().decode()
            ms = (time.time() - t0) * 1000
            try:
                return e.code, json.loads(txt), ms
            except Exception:
                return e.code, {"raw": txt}, ms
        except Exception as e:
            return 0, {"error": str(e)}, (time.time() - t0) * 1000

    def get(self, path, token=None):
        return self.req("GET", path, token=token)

    def post(self, path, body=None, token=None, **kw):
        return self.req("POST", path, body=body if body is not None else {}, token=token, **kw)


class Recorder:
    def __init__(self, part):
        self.part = part
        self.results = []
        self.latencies = {}

    def add(self, sid, module, title, priority, ok, note=""):
        self.results.append({
            "id": sid, "module": module, "title": title, "priority": priority,
            "result": "Pass" if ok else "Fail", "note": note
        })
        print(f"[{'PASS' if ok else 'FAIL'}] {sid} — {note[:110]}")

    def latency(self, name, ms):
        self.latencies.setdefault(name, []).append(round(ms))

    def save(self):
        path = os.path.join(HERE, f"results_{self.part}.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump({"results": self.results, "latencies": self.latencies}, f, indent=1)
        print(f"\nSaved {len(self.results)} hasil -> {path}")


def uniq(prefix):
    return f"{prefix}{uuid.uuid4().hex[:8]}@demo.com".lower()


def register_verify_login(api, email, password="Demo1234!", name="UAT Tester", phone=None):
    """Registrasi + verifikasi OTP (dev_otp) + login. Return (token, otp1) atau raise."""
    st, j, _ = api.post("/api/v1/auth/register",
                        {"email": email, "password": password, "fullName": name, "phoneNumber": phone})
    assert st == 200, f"register gagal {st} {j}"
    otp = j["data"]["dev_otp"]
    st, j, _ = api.post("/api/v1/auth/verify-otp", {"email": email, "otpCode": otp})
    assert st == 200, f"verify gagal {st} {j}"
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": email, "password": password})
    assert st == 200, f"login gagal {st} {j}"
    return j["data"]["access_token"], otp


_period_counter = [0]


def create_paid_booking(api, token, room="RM-BDG-STD", days_in=None, days_out=None, guest="UAT Guest",
                        method="VA", guest_count=2, expected_price=None, hotel_id="HTL-BDG-001"):
    """Buat booking + bayar sampai PAID. Return dict booking/payment/invoice info.
    days_in default: periode unik berurutan (hindari kehabisan inventory antar skenario)."""
    from datetime import date, timedelta
    if days_in is None:
        days_in = 14 + _period_counter[0] * 2
        _period_counter[0] += 1
    if days_out is None:
        days_out = days_in + 3
    ci = (date.today() + timedelta(days=days_in)).isoformat()
    co = (date.today() + timedelta(days=days_out)).isoformat()
    body = {"hotelId": hotel_id, "roomId": room, "guestName": guest,
            "checkIn": ci, "checkOut": co, "guestCount": guest_count}
    if expected_price is not None:
        body["expectedPricePerNight"] = expected_price
    st, j, ms = api.post("/api/v1/bookings", body, token=token)
    if st != 200:
        raise AssertionError(f"booking gagal {st} {j}")
    booking = j["data"]
    st, j, _ = api.post("/api/v1/payments", {"bookingId": booking["bookingId"], "method": method}, token=token)
    assert st == 200, f"payment gagal {st} {j}"
    payment = j["data"]
    st, j, _ = api.post(f"/api/v1/payments/{payment['paymentId']}/simulate-callback?status=SUCCESS", {}, token=token)
    assert st == 200, f"callback gagal {st} {j}"
    return {"booking": booking, "payment": payment, "callback": j["data"]}


def login(api, email, password="Demo1234!"):
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": email, "password": password})
    return st, j
