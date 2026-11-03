"""UAT runner bagian 3 — server http://localhost:5211 (timer dipercepat).
REG-06 (OTP expiry), REG-07 (resend), LOG-03 (lockout), LOG-04 (unlock)."""
import sys, os, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.modules.pop("uatlib", None)
from uatlib import Api, Recorder, uniq, register_verify_login, login

BASE = "http://localhost:5211"
api = Api(BASE)
rec = Recorder("timed_a")


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


def reg06():
    email = uniq("reg06")
    st, j, _ = api.post("/api/v1/auth/register", {"email": email, "password": "Demo1234!"})
    otp = j["data"]["dev_otp"]
    print("  menunggu 65 detik hingga OTP kedaluwarsa (TTL dipercepat jadi 1 menit)...")
    time.sleep(65)
    st, j, _ = api.post("/api/v1/auth/verify-otp", {"email": email, "otpCode": otp})
    expect(st == 410 and j.get("code") == "OTP_EXPIRED", f"{st} {j}")
    st, j2 = login(api, email)
    expect(st == 403 and j2.get("code") == "ACCOUNT_NOT_VERIFIED", "akun tidak boleh aktif")
    return "OTP kedaluwarsa ditolak (OTP_EXPIRED), akun tidak diaktifkan, diminta OTP baru"


def reg07():
    email = uniq("reg07")
    st, j, _ = api.post("/api/v1/auth/register", {"email": email, "password": "Demo1234!"})
    otp1 = j["data"]["dev_otp"]
    time.sleep(2.5)  # cooldown dipercepat jadi 2 detik
    st, j, _ = api.post("/api/v1/auth/resend-otp", {"email": email})
    expect(st == 200, f"resend: {st} {j}")
    otp2 = j["data"]["dev_otp"]
    expect(otp1 != otp2, "OTP baru sama dengan OTP lama")
    st, j, _ = api.post("/api/v1/auth/verify-otp", {"email": email, "otpCode": otp1})
    expect(st == 401 and j.get("code") == "OTP_INVALID", f"OTP lama masih berlaku: {st} {j}")
    st, j, _ = api.post("/api/v1/auth/verify-otp", {"email": email, "otpCode": otp2})
    expect(st == 200 and j["data"]["account_status"] == "ACTIVE", f"OTP baru: {st} {j}")

    # batas maksimum OTP per jam (5)
    email2 = uniq("reg07b")
    st, j, _ = api.post("/api/v1/auth/register", {"email": email2, "password": "Demo1234!"})
    expect(st == 200, f"{st} {j}")
    last = None
    for i in range(4):
        time.sleep(2.5)
        st, j, _ = api.post("/api/v1/auth/resend-otp", {"email": email2})
        last = (st, j)
    expect(last[0] == 200, f"resend ke-5: {last}")
    time.sleep(2.5)
    st, j, _ = api.post("/api/v1/auth/resend-otp", {"email": email2})
    expect(st == 429 and j.get("code") == "TOO_MANY_REQUESTS", f"batas per jam: {st} {j}")
    return "OTP baru menggantikan OTP lama (yg lama ditolak); cooldown & batas 5x/jam dihormati"


def log03():
    email = uniq("log03")
    register_verify_login(api, email)
    for i in range(5):
        st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": email, "password": "Salah123!"})
        expect(st == 401, f"percobaan {i+1}: {st} {j}")
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": email, "password": "Demo1234!"})
    expect(st == 423 and j.get("code") == "ACCOUNT_LOCKED", f"percobaan ke-6 dengan password benar: {st} {j}")
    return "setelah 5x gagal akun terkunci; percobaan ke-6 dengan password BENAR pun ditolak (ACCOUNT_LOCKED)"


def log04():
    email = S_email
    print("  menunggu 65 detik hingga masa lock berakhir (lockout dipercepat jadi 1 menit)...")
    time.sleep(65)
    st, j = login(api, email)
    expect(st == 200 and j["data"]["access_token"], f"{st} {j}")
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": email, "password": "Salah123!"})
    expect(st == 401, f"counter tidak direset: {st} {j}")
    st, j = login(api, email)
    expect(st == 200, f"harusnya masih bisa login: {st} {j}")
    return "setelah masa lock berakhir login berhasil lagi & counter percobaan gagal direset"


run("UAT-REG-06", "MOD-01", "Verifikasi OTP setelah kedaluwarsa", "High", reg06)
run("UAT-REG-07", "MOD-01", "Resend OTP — OTP lama tidak berlaku", "High", reg07)

# LOG-03 memakai akun yang sama dengan LOG-04
S_email = uniq("log034")


def log03_wrap():
    global S_email
    register_verify_login(api, S_email)
    for i in range(5):
        st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": S_email, "password": "Salah123!"})
        expect(st == 401, f"percobaan {i+1}: {st} {j}")
    st, j, _ = api.post("/api/v1/auth/login", {"emailOrPhone": S_email, "password": "Demo1234!"})
    expect(st == 423 and j.get("code") == "ACCOUNT_LOCKED", f"percobaan ke-6 dengan password benar: {st} {j}")
    return "setelah 5x gagal akun terkunci; percobaan ke-6 dengan password BENAR pun ditolak (ACCOUNT_LOCKED)"


run("UAT-LOG-03", "MOD-02", "Akun terkunci setelah 5 kali login gagal", "Critical", log03_wrap)
run("UAT-LOG-04", "MOD-02", "Login berhasil kembali setelah masa lock berakhir", "High", log04)

rec.save()
print("PART TIMED-A selesai:", sum(1 for r in rec.results if r["result"] == "Pass"), "pass /",
      sum(1 for r in rec.results if r["result"] == "Fail"), "fail")
