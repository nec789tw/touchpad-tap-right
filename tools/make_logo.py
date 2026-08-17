# -*- coding: utf-8 -*-
"""TouchpadTapRight logo：觸控板圓角磚 ＋ 右下角高亮 ＋ 指尖輕觸波紋。
輸出：docs/assets/logo.svg（原稿）、logo-256.png、logo-banner.png、Resources/app.ico（16–256）。
用 PIL 直接畫（不靠 SVG 光柵器）,SVG 與 PIL 版本幾何一致。"""
import os
from PIL import Image, ImageDraw

ROOT = r"D:\python\已完成開發\特殊用途\觸控盤右鍵"
ASSETS = os.path.join(ROOT, "docs", "assets")
os.makedirs(ASSETS, exist_ok=True)

# ── 幾何（以 256 為基準）──
S = 256
PAD = (18, 40, 238, 216)          # 觸控板磚 x0,y0,x1,y1（2.5:1.6，接近真實觸控板）
R = 30                            # 圓角
ZX = 148                          # 右鍵區起點 x（磚寬的 59%）
ZY = 128                          # 上下分界 y（磚高的 50%）
TAP = (193, 172)                  # 指尖位置（右鍵區中央偏下）

# ── 色票（跟 Theme 淺色一致）──
PAD_FILL = (203, 213, 225)        # slate-300
PAD_EDGE = (71, 85, 105)          # slate-600
ZONE = (29, 78, 216)              # blue-700（Theme.Accent）
ZONE_LINE = (37, 99, 235)
SPLIT = (190, 18, 60)             # rose-700（Theme.PreviewSplit）
RIPPLE = (255, 255, 255)
FINGER = (255, 255, 255)

def draw(scale):
    """回傳 scale 倍超取樣的 RGBA 影像。"""
    W = S * scale
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    def s(v): return v * scale
    x0, y0, x1, y1 = [s(v) for v in PAD]
    # 陰影（讓淺底也看得到邊界）
    d.rounded_rectangle((x0 + s(3), y0 + s(4), x1 + s(3), y1 + s(4)), radius=s(R), fill=(15, 23, 42, 60))
    # 磚
    d.rounded_rectangle((x0, y0, x1, y1), radius=s(R), fill=PAD_FILL, outline=PAD_EDGE, width=s(4))
    # 右下角高亮（裁在圓角內：用遮罩）
    zone = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    zd = ImageDraw.Draw(zone)
    zd.rectangle((s(ZX), s(ZY), x1, y1), fill=ZONE)
    mask = Image.new("L", (W, W), 0)
    ImageDraw.Draw(mask).rounded_rectangle((x0 + s(2), y0 + s(2), x1 - s(2), y1 - s(2)), radius=s(R - 2), fill=255)
    img.paste(zone, (0, 0), Image.composite(zone.split()[3], Image.new("L", (W, W), 0), mask))
    d = ImageDraw.Draw(img)
    # 分界線
    d.line((s(ZX), y0 + s(4), s(ZX), y1 - s(4)), fill=ZONE_LINE, width=s(4))
    # 虛線上下分界
    dash = s(10); gap = s(7); x = x0 + s(6)
    while x < x1 - s(6):
        d.line((x, s(ZY), min(x + dash, x1 - s(6)), s(ZY)), fill=SPLIT, width=s(4))
        x += dash + gap
    # 指尖波紋（兩圈）＋指尖
    tx, ty = s(TAP[0]), s(TAP[1])
    for rr, a, w in ((s(34), 110, s(4)), (s(22), 190, s(5))):
        ring = Image.new("RGBA", (W, W), (0, 0, 0, 0))
        ImageDraw.Draw(ring).ellipse((tx - rr, ty - rr, tx + rr, ty + rr), outline=RIPPLE + (a,), width=w)
        img.alpha_composite(ring)
    d = ImageDraw.Draw(img)
    r = s(11)
    d.ellipse((tx - r, ty - r, tx + r, ty + r), fill=FINGER, outline=ZONE, width=s(3))
    return img

big = draw(8)                       # 2048px 超取樣
def down(size): return big.resize((size, size), Image.LANCZOS)

down(256).save(os.path.join(ASSETS, "logo-256.png"))
down(512).save(os.path.join(ASSETS, "logo-512.png"))
# ICO：多尺寸（Windows 資源管理器／工作列／Alt-Tab 各取一種）
sizes = [16, 24, 32, 48, 64, 128, 256]
# PIL 的 ICO 只會從「基底影像」往下縮,基底要用最大的 256
down(256).save(os.path.join(ROOT, "CSharp", "TouchpadRightClick", "Resources", "app.ico"),
               format="ICO", sizes=[(z, z) for z in sizes])
# README 橫幅：logo ＋ 文字（1200×320，透明底）
from PIL import ImageFont
def font(name, size):
    for f in (name, "msjhbd.ttc", "msjh.ttc", "segoeuib.ttf", "arialbd.ttf"):
        try: return ImageFont.truetype(f, size)
        except Exception: pass
    return ImageFont.load_default()
for suffix, c1, c2 in (("", (17, 24, 39), (75, 85, 99)), ("-dark", (243, 244, 246), (156, 163, 175))):
    banner = Image.new("RGBA", (1200, 320), (0, 0, 0, 0))
    banner.alpha_composite(down(256), (32, 32))
    bd = ImageDraw.Draw(banner)
    bd.text((330, 70), "TouchpadTapRight", font=font("segoeuib.ttf", 84), fill=c1)
    bd.text((334, 178), "觸控板輕觸右鍵 ｜ 單指輕觸就能按右鍵", font=font("msjhbd.ttc", 44), fill=c2)
    banner.save(os.path.join(ASSETS, "logo-banner" + suffix + ".png"))

# SVG 原稿（同幾何）
svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {S} {S}" width="{S}" height="{S}">
  <title>TouchpadTapRight</title>
  <defs><clipPath id="pad"><rect x="{PAD[0]+2}" y="{PAD[1]+2}" width="{PAD[2]-PAD[0]-4}" height="{PAD[3]-PAD[1]-4}" rx="{R-2}"/></clipPath></defs>
  <rect x="{PAD[0]+3}" y="{PAD[1]+4}" width="{PAD[2]-PAD[0]}" height="{PAD[3]-PAD[1]}" rx="{R}" fill="#0F172A" opacity="0.24"/>
  <rect x="{PAD[0]}" y="{PAD[1]}" width="{PAD[2]-PAD[0]}" height="{PAD[3]-PAD[1]}" rx="{R}" fill="#CBD5E1" stroke="#475569" stroke-width="4"/>
  <rect x="{ZX}" y="{ZY}" width="{PAD[2]-ZX}" height="{PAD[3]-ZY}" fill="#1D4ED8" clip-path="url(#pad)"/>
  <line x1="{ZX}" y1="{PAD[1]+4}" x2="{ZX}" y2="{PAD[3]-4}" stroke="#2563EB" stroke-width="4"/>
  <line x1="{PAD[0]+6}" y1="{ZY}" x2="{PAD[2]-6}" y2="{ZY}" stroke="#BE123C" stroke-width="4" stroke-dasharray="10 7"/>
  <circle cx="{TAP[0]}" cy="{TAP[1]}" r="34" fill="none" stroke="#FFFFFF" stroke-opacity="0.43" stroke-width="4"/>
  <circle cx="{TAP[0]}" cy="{TAP[1]}" r="22" fill="none" stroke="#FFFFFF" stroke-opacity="0.75" stroke-width="5"/>
  <circle cx="{TAP[0]}" cy="{TAP[1]}" r="11" fill="#FFFFFF" stroke="#1D4ED8" stroke-width="3"/>
</svg>
'''
open(os.path.join(ASSETS, "logo.svg"), "w", encoding="utf-8").write(svg)
print("done:", os.listdir(ASSETS))
