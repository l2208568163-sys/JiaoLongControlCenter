# UI 重打包编排 v2（app3 = 官方原包 + 补丁1/2/3/4 + HomeMod 注入库）
# 2026-10-13 增加：JiaoLongLogoMod.dll + MQTTnet.dll 双注入 + deps.json 双登记
import os, re, shutil, subprocess, sys, json

WORK = r"F:\JiaoLongControlCenter\_work\msix_patch"
KIT = r"C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64"
MAKEAPPX = os.path.join(KIT, "makeappx.exe")
SIGNSIGN = os.path.join(KIT, "signtool.exe")
ZIP = os.path.join(WORK, "CCU.WinUI.zip")
APP3 = os.path.join(WORK, "app3")
LOGO_OUT = r"F:\JiaoLongControlCenter\_work\logomod\bin\Release\net8.0-windows10.0.22621.0"
MOD_DLLS = ["JiaoLongLogoMod.dll"]   # 全部复制进包 + deps.json 登记（M2Mqtt 官方包内已有，不重复带）
PATCHER = r"F:\JiaoLongControlCenter\_work\patcher\bin\Debug\net10.0\DllPatch.dll"
PFX = os.path.join(WORK, "JiaoLongCCU-Port.pfx")
PFX_PWD = "JiaoLongCCU-Port-2026"
OUT_MSIX = os.path.join(WORK, "CCU.WinUI.ported.msix")

def run(cmd, **kw):
    print("$", " ".join(cmd))
    r = subprocess.run(cmd, capture_output=True, text=True, timeout=kw.pop("timeout", 300), **kw)
    out = (r.stdout or "").strip()
    err = (r.stderr or "").strip()
    if out: print(out[-2000:])
    if err: print("STDERR:", err[-2000:])
    if r.returncode != 0:
        raise SystemExit(f"FAILED rc={r.returncode}: {' '.join(cmd)}")
    return r

# ---------- 1. 全新解包官方原包 ----------
if os.path.exists(APP3):
    shutil.rmtree(APP3)
run([MAKEAPPX, "unpack", "/p", ZIP, "/d", APP3, "/o"])

# ---------- 2. 注入库 DLL 进包 ----------
for dll in MOD_DLLS:
    shutil.copy2(os.path.join(LOGO_OUT, dll), os.path.join(APP3, dll))
    print(f"copied {dll} ({os.path.getsize(os.path.join(APP3, dll))}B)")

# ---------- 3. dnlib 四补丁 ----------
run(["dotnet", PATCHER, os.path.join(APP3, "CCUWinUI.dll"), os.path.join(APP3, "JiaoLongLogoMod.dll")])

# ---------- 4. deps.json 登记（两个 target 段 + libraries，幂等；空段不带逗号） ----------
deps_path = os.path.join(APP3, "CCUWinUI.deps.json")
txt = open(deps_path, encoding="utf-8").read()
n = 0
for dll in MOD_DLLS:
    name = os.path.splitext(dll)[0]
    if name in txt:
        print(f"deps.json: {name} already registered")
        continue
    entry_t = f'"{name}/1.0.0.0": {{"runtime": {{"{dll}": {{}}}}}}'
    entry_l = f'"{name}/1.0.0.0": {{"type": "project", "serviceable": false, "sha512": ""}}'
    for pat in (r'(".NETCoreApp,Version=v8\.0(?:/win-x64)?":\s*\{)', r'("libraries":\s*\{)'):
        def repl(m):
            global n
            n += 1
            entry = entry_l if "libraries" in m.group(1) else entry_t
            after = txt[m.end():].lstrip()
            comma = "" if after.startswith("}") else ","
            return m.group(1) + "\n      " + entry + comma
        txt = re.sub(pat, repl, txt)
    print(f"deps.json: {name} registered in {n} section(s) (running total)")
open(deps_path, "w", encoding="utf-8").write(txt)
json.load(open(deps_path, encoding="utf-8"))  # 合法性校验
total_sections = 3 * len(MOD_DLLS)
if n != total_sections:
    raise SystemExit(f"deps.json sections matched: {n}, expected {total_sections}")

# ---------- 5. manifest Publisher -> CN=JiaoLongCCU-Port ----------
mf_path = os.path.join(APP3, "AppxManifest.xml")
mf = open(mf_path, encoding="utf-8").read()
old_pub = 'Publisher="CN=AISTONE GLOBAL (SUZHOU) LIMITED, O=AISTONE GLOBAL (SUZHOU) LIMITED, L=Suzhou, S=Jiangsu, C=CN, OID.1.3.6.1.4.1.311.60.2.1.1=Suzhou, OID.1.3.6.1.4.1.311.60.2.1.2=Jiangsu, OID.1.3.6.1.4.1.311.60.2.1.3=CN, SERIALNUMBER=91320594321285131E, OID.2.5.4.15=Private Organization"'
if old_pub in mf:
    mf = mf.replace(old_pub, 'Publisher="CN=JiaoLongCCU-Port"')
    open(mf_path, "w", encoding="utf-8").write(mf)
    print("manifest: Publisher -> CN=JiaoLongCCU-Port")
elif 'Publisher="CN=JiaoLongCCU-Port"' in mf:
    print("manifest: Publisher already JiaoLongCCU-Port")
else:
    raise SystemExit("manifest: AISTONE publisher pattern not found")

# ---------- 6. 删除 BlockMap / 签名（打包器重建） ----------
for f in ("AppxBlockMap.xml", "AppxSignature.p7x"):
    p = os.path.join(APP3, f)
    if os.path.exists(p):
        os.remove(p)
        print("removed", f)

# makeappx unpack 不落 [Content_Types].xml；572 - 3 + 注入库数
n_files = sum(len(fs) for _, _, fs in os.walk(APP3))
expect = 572 - 3 + len(MOD_DLLS)
print(f"app3 ready: {n_files} files (expect {expect})")
if n_files != expect:
    raise SystemExit(f"unexpected file count: {n_files}")

# ---------- 7. makeappx pack ----------
if os.path.exists(OUT_MSIX):
    os.remove(OUT_MSIX)
run([MAKEAPPX, "pack", "/d", APP3, "/p", OUT_MSIX, "/o"])

# ---------- 8. signtool sign + verify ----------
run([SIGNSIGN, "sign", "/fd", "SHA256", "/f", PFX, "/p", PFX_PWD, OUT_MSIX])
run([SIGNSIGN, "verify", "/pa", OUT_MSIX])
print("ALL OK ->", OUT_MSIX, os.path.getsize(OUT_MSIX), "bytes")
