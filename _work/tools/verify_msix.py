# 签名包回解包 + 全量 payload 哈希比对（§8 事故防线）
import os, hashlib, shutil, subprocess, sys

WORK = r"F:\JiaoLongControlCenter\_work\msix_patch"
KIT = r"C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64"
MAKEAPPX = os.path.join(KIT, "makeappx.exe")
APP3 = os.path.join(WORK, "app3")
VERIFY = os.path.join(WORK, "_unpack_verify3")
MSIX = os.path.join(WORK, "CCU.WinUI.ported.msix")
# makeappx pack 丢弃 AppxMetadata/CodeIntegrity.cat，signtool 签名时按新签名重建
# （与 BlockMap/Signature/ContentTypes 同属打包器/签名器生成物，不参与 payload 比对）
GENERATED = {"AppxBlockMap.xml", "AppxSignature.p7x", "[Content_Types].xml", "AppxMetadata/CodeIntegrity.cat"}

def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()

def scan(root):
    out = {}
    for dp, dn, fn in os.walk(root):
        for f in fn:
            rel = os.path.relpath(os.path.join(dp, f), root).replace(os.sep, "/")
            out[rel] = os.path.join(dp, f)
    return out

if os.path.exists(VERIFY):
    shutil.rmtree(VERIFY)
r = subprocess.run([MAKEAPPX, "unpack", "/p", MSIX, "/d", VERIFY, "/o"],
                   capture_output=True, text=True, timeout=300)
if r.returncode != 0:
    print(r.stdout[-1500:], r.stderr[-1500:])
    sys.exit("unpack verify FAILED")

src = scan(APP3)
dst = scan(VERIFY)
mismatch, missing = [], []
for rel, sp in src.items():
    if rel in GENERATED:
        continue  # 打包器/签名器生成物，源目录里的原始值不参与比对
    dp = dst.get(rel)
    if dp is None:
        missing.append(rel)
    elif sha(sp) != sha(dp):
        mismatch.append(rel)
extras = sorted(set(dst) - set(src) - GENERATED)
print(f"app3 payload files : {len(src)}")
print(f"unpacked files     : {len(dst)}")
print(f"hash mismatch      : {len(mismatch)} {mismatch[:5]}")
print(f"missing in package : {len(missing)} {missing[:5]}")
print(f"unexpected extras  : {len(extras)} {extras[:5]}")
if mismatch or missing or extras:
    sys.exit("VERIFY FAILED")
print("VERIFY PASSED: 全部 payload 文件与源目录逐字节一致")
