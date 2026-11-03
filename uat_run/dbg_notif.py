
import json, urllib.request
def req(method, path, body=None, token=None):
    r = urllib.request.Request("http://localhost:5210"+path,
        data=json.dumps(body).encode() if body is not None else None,
        headers={"Content-Type":"application/json", **({"Authorization":"Bearer "+token} if token else {})}, method=method)
    try:
        with urllib.request.urlopen(r, timeout=15) as resp: return resp.status, json.loads(resp.read().decode())
    except urllib.error.HTTPError as e: return e.code, json.loads(e.read().decode())
import urllib.error
_, j = req("POST","/api/v1/auth/login",{"emailOrPhone":"admin@demo.com","password":"Demo1234!"})
tok = j["data"]["access_token"]
_, j = req("GET","/api/v1/admin/notifications?size=5", token=tok)
print(json.dumps(j["data"]["items"][:3], indent=1))
