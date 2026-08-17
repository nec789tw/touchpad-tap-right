# -*- coding: utf-8 -*-
"""把專案的開發文件（*.md）產生成單一份可視覺化的交接網頁。

用法：
    PYTHONIOENCODING=utf-8 PYTHONUTF8=1 python tools/產生交接網頁.py
    → 產生 docs/交接手冊.html（單檔、離線可開、無外部相依）

設計要點：
  * **產生式**：內容一律來自 *.md，改了 md 重跑本檔即同步；HTML 請勿手改。
  * 純標準庫、零相依（與專案「離線可跑」一致）；自帶極簡 Markdown 轉譯器。
  * 待辦統計：掃描 `- [ ]` 算「尚有幾項未完成」（未完成總表的慣例是做完就整項移除、
    不留打勾，完成紀錄改記在 docs/工作狀態.md）；嚴重度【高/中/低】自動標色。
  * 供開發團隊交接：側欄導覽、全文搜尋、列印友善、深淺色自動。
  * 本專案文件無個資，可隨 repo 公開。
"""
import datetime
import html
import os
import re
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(HERE, "docs", "交接手冊.html")
CUR_MD_DIR = HERE   # main() 每轉一份 md 前更新：圖片相對路徑要從「md 所在處」換算成「HTML 所在處」


def _rel_asset(url):
    """把 md 裡的相對圖片路徑換算成相對於輸出 HTML 的路徑；http(s)/data:/絕對路徑原樣。"""
    if re.match(r"^(https?:|data:|/|[A-Za-z]:)", url):
        return url
    src = os.path.normpath(os.path.join(CUR_MD_DIR, url))
    return os.path.relpath(src, os.path.dirname(OUT)).replace(os.sep, "/")


# 原始 HTML 區塊：只放行這些「文件裡真的會用到」的標籤（picture/img 給 README 的 logo 用），
# 其餘 < 開頭的行仍當文字跳脫——這是自家文件，不是使用者輸入，白名單只是防手滑。
_RAW_HTML_TAGS = r"picture|source|img|details|summary|div|br|kbd|sup|sub|figure|figcaption|video|audio"
_RAW_HTML_RE = re.compile(r"^\s*<(%s)\b" % _RAW_HTML_TAGS, re.I)


def _fix_raw_html_paths(block):
    """原始 HTML 內的 src / srcset 相對路徑一併換算。"""
    return re.sub(r'\b(src|srcset)="([^"]+)"',
                  lambda m: '%s="%s"' % (m.group(1), _rel_asset(m.group(2))), block)

# 收錄順序＝交接閱讀順序（新人由上而下讀）
DOCS = [
    ("README.md", "專案簡介", "這是什麼、給誰用、怎麼下載／編譯／使用"),
    ("docs/未完成盤點總表.md", "未完成總表", "★ 交接第一份：還有什麼沒做、卡在哪"),
    ("docs/工作狀態.md", "工作狀態", "完成軌跡與決策紀錄（隨工作更新）"),
    ("docs/測試指南.md", "社群測試指南", "沒實機時靠社群回報的 7 步測法與回報方式"),
    ("docs/architecture.md", "架構說明", "模組職責、事件流、執行緒模型"),
    ("CLAUDE.md", "開發日誌", "各版本改了什麼、為什麼、開發規範"),
    ("ATTRIBUTION.md", "第三方聲明", "引用的 API、範例與授權"),
]


# ── 極簡 Markdown → HTML（涵蓋本專案文件實際用到的語法）──
def inline(s):
    """行內語法：程式碼、粗體、刪除線、連結；其餘一律跳脫。

    程式碼先抽成佔位符再套其他規則——否則像 `**雙擊 `x.bat`**` 這種
    「粗體內含行內碼」會被切成兩段，兩邊各剩一個 ** 而轉不出來。
    """
    codes = []

    def _stash(m):
        codes.append(html.escape(m.group(1)))
        return "\x00%d\x00" % (len(codes) - 1)

    s = re.sub(r"`([^`]+)`", _stash, s)
    s = _inline_no_code(s)
    return re.sub(r"\x00(\d+)\x00", lambda m: "<code>%s</code>" % codes[int(m.group(1))], s)


def _inline_no_code(s):
    s = html.escape(s)
    # 圖片要先於連結處理（語法只差一個 !）。本地圖片內嵌；外部圖片（徽章）離線本來就看不到，退成文字連結
    def _img(m):
        alt, url = m.group(1), m.group(2)
        if re.match(r"^https?:", url):
            return '<a href="%s" class="img-link">[%s]</a>' % (url, alt or url)
        return '<img src="%s" alt="%s" class="md-img">' % (_rel_asset(url), alt)
    # 連結包圖片 [![alt](img)](href)（README 徽章慣用寫法）：外部圖→「alt」文字連到 href；本地圖→可點的圖
    def _linked_img(m):
        alt, img, href = m.group(1), m.group(2), m.group(3)
        if re.match(r"^https?:", img):
            return '<a href="%s" class="img-link">[%s]</a>' % (href, alt or href)
        return '<a href="%s"><img src="%s" alt="%s" class="md-img"></a>' % (href, _rel_asset(img), alt)
    s = re.sub(r"\[!\[([^\]]*)\]\(([^)\s]+)\)\]\(([^)]+)\)", _linked_img, s)
    s = re.sub(r"!\[([^\]]*)\]\(([^)\s]+)(?:\s+&quot;[^&]*&quot;)?\)", _img, s)
    s = re.sub(r"\[([^\]]+)\]\(([^)]+)\)",
               lambda m: '<a href="%s">%s</a>' % (m.group(2), m.group(1)), s)
    # 非貪婪且允許內含單一 *（文件常見 *.md／*.db，用 [^*]+ 會整段失配）
    s = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", s)
    s = re.sub(r"~~([^~]+)~~", r"<del>\1</del>", s)
    # 嚴重度徽章：【高】【中】【低】與「高/中/低」單格
    s = re.sub(r"【(高|中|低)([^】]*)】",
               lambda m: '<span class="sev sev-%s">%s%s</span>' % (
                   {"高": "hi", "中": "mid", "低": "lo"}[m.group(1)], m.group(1), m.group(2)), s)
    return s


# 超過這個字數的文件，h2 章節預設收合成一行標題——攤開來滾不完（一份 20 篇的手冊
# 攤平有六萬像素高，等於逼人用捲軸找東西）。收合只改「怎麼讀」不改「有什麼」：
# 內容一條都沒少（那些是別人踩過的坑），搜尋照樣搜得到（命中的章節會自動展開）。
# 2,500 字以下的短文件（待辦清單、備份 SOP）維持攤開，一打開就看得到。
FOLD_MIN_CHARS = 2500


def md_to_html(md, idprefix, fold=False):
    """回傳 (html, 章節清單[(層級, 文字, anchor)], 待辦統計(done, total))。

    fold=True 時把每個 h2 章節包成 <details>（預設收合，點標題展開）。
    """
    lines = md.replace("\r\n", "\n").split("\n")
    out, toc = [], []
    done = total = 0
    i, n = 0, len(lines)
    list_stack = []          # 開著的清單層級（縮排空白數）
    chap_open = [False]      # 目前是否有開著的 <details> 章節

    def close_lists(to=0):
        while list_stack and list_stack[-1] >= to:
            out.append("</ul>")
            list_stack.pop()

    def close_chap():
        if chap_open[0]:
            out.append("</div></details>")
            chap_open[0] = False

    while i < n:
        ln = lines[i]

        # 程式碼區塊
        if ln.strip().startswith("```"):
            i += 1
            buf = []
            while i < n and not lines[i].strip().startswith("```"):
                buf.append(lines[i]); i += 1
            i += 1
            close_lists()
            out.append("<pre><code>%s</code></pre>" % html.escape("\n".join(buf)))
            continue

        # 表格（含表頭分隔列）
        if ln.lstrip().startswith("|") and i + 1 < n and re.match(r"^\s*\|[\s:|-]+\|\s*$", lines[i + 1]):
            close_lists()
            head = [c.strip() for c in ln.strip().strip("|").split("|")]
            i += 2
            rows = []
            while i < n and lines[i].lstrip().startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            out.append('<div class="tw"><table><thead><tr>%s</tr></thead><tbody>%s</tbody></table></div>' % (
                "".join("<th>%s</th>" % inline(c) for c in head),
                "".join("<tr>%s</tr>" % "".join("<td>%s</td>" % inline(c) for c in r) for r in rows)))
            continue

        # 標題
        m = re.match(r"^(#{1,4})\s+(.*)$", ln)
        if m:
            close_lists()
            lvl, txt = len(m.group(1)), m.group(2).strip()
            anchor = "%s-%d" % (idprefix, len(toc))
            toc.append((lvl, re.sub(r"<[^>]+>", "", txt), anchor))
            if fold and lvl == 2:
                close_chap()
                out.append(
                    '<details class="chap"><summary id="%s"><span class="ct">%s</span></summary>'
                    '<div class="cbody">' % (anchor, inline(txt)))
                chap_open[0] = True
            else:
                out.append('<h%d id="%s">%s</h%d>' % (
                    min(lvl + 1, 6), anchor, inline(txt), min(lvl + 1, 6)))
            i += 1
            continue

        # 水平線
        if re.match(r"^\s*---+\s*$", ln):
            close_lists(); out.append("<hr>"); i += 1; continue

        # 引言（內容可含清單／待辦，故遞迴解析，待辦亦計入統計）
        if ln.lstrip().startswith(">"):
            close_lists()
            buf = []
            while i < n and (lines[i].lstrip().startswith(">") or
                             (buf and lines[i].strip() and not lines[i].lstrip().startswith(("#", "-", "*", "|")))):
                raw = lines[i].lstrip()
                buf.append(raw[1:].lstrip() if raw.startswith(">") else raw)
                i += 1
            inner, _, (d2, t2) = md_to_html("\n".join(buf), idprefix + "q%d" % i)
            done += d2; total += t2
            out.append("<blockquote>%s</blockquote>" % inner)
            continue

        # 清單（含待辦 checkbox）
        m = re.match(r"^(\s*)([-*]|\d+\.)\s+(.*)$", ln)
        if m:
            indent, body = len(m.group(1)), m.group(3)
            while list_stack and list_stack[-1] > indent:
                out.append("</ul>"); list_stack.pop()
            if not list_stack or list_stack[-1] < indent:
                out.append("<ul>"); list_stack.append(indent)
            cb = re.match(r"^\[([ xX])\]\s*(.*)$", body)
            if cb:
                total += 1
                checked = cb.group(1).lower() == "x"
                if checked:
                    done += 1
                out.append('<li class="task %s"><span class="cb">%s</span>%s</li>' % (
                    "done" if checked else "open", "✓" if checked else "", inline(cb.group(2))))
            else:
                out.append("<li>%s</li>" % inline(body))
            i += 1
            continue

        # 原始 HTML 區塊（README 的 <picture> logo 之類）：整段原樣輸出到空行為止，只換算 src 路徑
        if _RAW_HTML_RE.match(ln):
            close_lists()
            buf = []
            while i < n and lines[i].strip():
                buf.append(lines[i]); i += 1
            out.append('<div class="raw-html">%s</div>' % _fix_raw_html_paths("\n".join(buf)))
            continue

        # 空行／段落
        if not ln.strip():
            close_lists(); i += 1; continue
        close_lists()
        buf = [ln]
        i += 1
        while i < n and lines[i].strip() and not re.match(r"^(\s*([-*]|\d+\.)\s|#{1,4}\s|\s*\||>|```|\s*---+\s*$)", lines[i]):
            buf.append(lines[i]); i += 1
        out.append("<p>%s</p>" % inline(" ".join(x.strip() for x in buf)))

    close_lists()
    close_chap()
    return "\n".join(out), toc, (done, total)


def main():
    docs = []
    for rel, title, desc in DOCS:
        path = os.path.join(HERE, rel.replace("/", os.sep))
        if not os.path.exists(path):
            print("略過（不存在）：", rel)
            continue
        with open(path, encoding="utf-8") as f:
            md = f.read()
        global CUR_MD_DIR
        CUR_MD_DIR = os.path.dirname(path)
        idp = "d%d" % len(docs)
        folded = len(md) >= FOLD_MIN_CHARS
        body, toc, (done, total) = md_to_html(md, idp, fold=folded)
        docs.append({"rel": rel, "title": title, "desc": desc, "id": idp, "body": body,
                     "folded": folded,
                     "toc": toc, "done": done, "total": total,
                     "mtime": datetime.datetime.fromtimestamp(os.path.getmtime(path)).strftime("%Y-%m-%d %H:%M")})

    # 待辦以「剩餘未完成」計（未完成總表的慣例是做完就移除，不留打勾項），
    # 故顯示「尚有 N 項」而非完成百分比。
    tot_open = sum(d["total"] - d["done"] for d in docs)
    tot_done = sum(d["done"] for d in docs)
    tot_all = sum(d["total"] for d in docs)
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")

    nav = "\n".join(
        '<a class="navitem" href="#%s"><span class="nt">%s</span>'
        '<span class="nd">%s</span>%s</a>' % (
            d["id"], html.escape(d["title"]), html.escape(d["desc"]),
            ('<span class="npill">%d 項</span>' % (d["total"] - d["done"]))
            if (d["total"] - d["done"]) else "")
        for d in docs)

    sections = []
    for d in docs:
        sub = "".join(
            '<a class="sub l%d" href="#%s">%s</a>' % (min(t[0], 3), t[2], html.escape(t[1]))
            for t in d["toc"] if t[0] >= 2)
        bar = ""
        _open = d["total"] - d["done"]
        if _open:
            bar = '<div class="prog"><span class="todo">尚有 %d 項待辦</span></div>' % _open
        elif d["done"]:
            bar = '<div class="prog"><span>本檔為完成紀錄（%d 項）</span></div>' % d["done"]
        sections.append(
            '<section class="doc" id="%s" data-title="%s">'
            '<header class="dh"><div><h2>%s</h2><p class="dd">%s</p></div>'
            '<div class="meta"><code>%s</code><span>更新 %s</span></div></header>'
            '%s%s%s<div class="body">%s</div></section>' % (
                d["id"], html.escape(d["title"]), html.escape(d["title"]), html.escape(d["desc"]),
                html.escape(d["rel"]), d["mtime"], bar,
                ('<nav class="subnav">%s</nav>' % sub) if sub else "",
                ('<div class="foldbar"><button data-fold="open">展開全部</button>'
                 '<button data-fold="close">收合全部</button></div>') if d["folded"] else "",
                d["body"]))

    tpl = """<!DOCTYPE html>
<html lang="zh-Hant">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>TouchpadTapRight 觸控板輕觸右鍵 ｜ 開發交接手冊</title>
<style>
:root{
  --ink:#151a21; --ink2:#3d4753; --mute:#6b7684; --faint:#98a2ae;
  --paper:#f7f5f0; --card:#fffdf9; --line:#e2ddd2; --line2:#cfc8b8;
  --jade:#1f6f5c; --jade-s:#e6f0ec; --clay:#a4472f; --amber:#8a6512;
  --mono:ui-monospace,"Cascadia Mono",Consolas,monospace;
  --serif:"Noto Serif TC","Songti TC",Georgia,serif;
  --sans:"Noto Sans TC","Microsoft JhengHei",-apple-system,"Segoe UI",sans-serif;
}
@media (prefers-color-scheme:dark){:root{
  --ink:#e9e6df; --ink2:#c2bcb1; --mute:#95908a; --faint:#736e68;
  --paper:#14161a; --card:#1b1e23; --line:#2c3037; --line2:#3d434c;
  --jade:#5cc0a3; --jade-s:#17302a; --clay:#e08a6f; --amber:#d6a83a;
}}
*{box-sizing:border-box}
body{margin:0;background:var(--paper);color:var(--ink);font-family:var(--sans);
  font-size:15px;line-height:1.75;-webkit-font-smoothing:antialiased}
.wrap{display:grid;grid-template-columns:288px 1fr;min-height:100vh}
/* 側欄 */
aside{position:sticky;top:0;height:100vh;overflow-y:auto;background:var(--card);
  border-right:1px solid var(--line);padding:22px 16px}
.brand{font-family:var(--serif);font-size:20px;font-weight:700;letter-spacing:.5px;margin:0 0 2px}
.brand small{display:block;font-family:var(--sans);font-size:11.5px;font-weight:400;
  color:var(--mute);letter-spacing:.08em;margin-top:3px}
.gen{font-size:11px;color:var(--faint);margin:14px 0 16px;padding-bottom:14px;border-bottom:1px solid var(--line)}
.stat{display:flex;align-items:baseline;gap:8px;margin-bottom:6px}
.stat b{font-size:34px;font-family:var(--mono);font-variant-numeric:tabular-nums;color:var(--jade)}
.stat span{font-size:12px;color:var(--mute)}
.stat small{font-size:10.5px;color:var(--faint)}
.gbar{height:5px;border-radius:3px;background:var(--line);overflow:hidden;margin:8px 0 18px}
.gbar i{display:block;height:100%;background:var(--jade)}
#q{width:100%;padding:8px 10px;border:1px solid var(--line2);border-radius:7px;
  background:var(--paper);color:var(--ink);font-family:var(--sans);font-size:13px;margin-bottom:14px}
#q:focus{outline:2px solid var(--jade);outline-offset:1px}
.navitem{display:block;padding:9px 11px;border-radius:8px;text-decoration:none;color:var(--ink);
  margin-bottom:3px;border:1px solid transparent;position:relative}
.navitem:hover{background:var(--jade-s);border-color:var(--line)}
.navitem.on{background:var(--jade-s);border-color:var(--jade)}
.nt{display:block;font-size:13.5px;font-weight:600}
.nd{display:block;font-size:11px;color:var(--mute);line-height:1.45;margin-top:1px}
.npill{position:absolute;right:9px;top:9px;font-family:var(--mono);font-size:10.5px;
  font-variant-numeric:tabular-nums;color:var(--mute);background:var(--paper);
  border:1px solid var(--line);border-radius:20px;padding:1px 7px}
/* 主體 */
main{padding:34px 42px 90px;max-width:1080px}
.md-img,.raw-html img{max-width:100%;height:auto;display:block;margin:8px 0}
.raw-html{margin:8px 0}
.img-link{font-size:12px;color:var(--ink2)}
.hero{border-bottom:2px solid var(--ink);padding-bottom:18px;margin-bottom:8px}
.hero h1{font-family:var(--serif);font-size:31px;margin:0 0 6px;letter-spacing:.5px}
.hero p{margin:0;color:var(--ink2);font-size:14px}
.warn{margin:18px 0 0;padding:11px 14px;border-left:3px solid var(--clay);
  background:var(--card);border-radius:0 8px 8px 0;font-size:12.5px;color:var(--ink2)}
.doc{margin-top:52px;scroll-margin-top:16px}
.dh{display:flex;justify-content:space-between;align-items:flex-end;gap:20px;
  border-bottom:1px solid var(--line2);padding-bottom:10px}
.dh h2{font-family:var(--serif);font-size:24px;margin:0}
.dd{margin:3px 0 0;font-size:12.5px;color:var(--mute)}
.meta{text-align:right;font-size:11px;color:var(--faint);white-space:nowrap}
.meta code{display:block;font-family:var(--mono);font-size:11px;color:var(--mute)}
.prog{display:flex;align-items:center;gap:10px;margin:12px 0 4px;font-size:12px;color:var(--mute)}
.prog .todo{background:var(--clay);color:#fff;border-radius:20px;padding:2px 11px;font-weight:600}
.bar{flex:1;max-width:260px;height:6px;background:var(--line);border-radius:3px;overflow:hidden}
.bar i{display:block;height:100%;background:var(--jade)}
.subnav{display:flex;flex-wrap:wrap;gap:6px;margin:12px 0 4px}
/* 章節摺疊：長文件的 h2 收成一行，點開才展開（內容一條沒少，只是不一次全攤開） */
.chap{border-top:1px solid var(--line);margin:0}
.chap:last-of-type{border-bottom:1px solid var(--line)}
.chap>summary{cursor:pointer;padding:11px 4px;list-style:none;display:flex;align-items:center;gap:9px;
  font-family:var(--serif);font-size:17px;font-weight:700;color:var(--ink);user-select:none}
.chap>summary::-webkit-details-marker{display:none}
.chap>summary::before{content:"▸";color:var(--muted);font-size:13px;transition:transform .15s;flex:none}
.chap[open]>summary::before{transform:rotate(90deg)}
.chap>summary:hover{color:var(--brand)}
.chap>summary:hover::before{color:var(--brand)}
.chap[open]>summary{border-bottom:1px dashed var(--line)}
.cbody{padding:2px 0 18px}
.cbody>h3:first-child,.cbody>h4:first-child{margin-top:12px}
.foldbar{display:flex;gap:8px;margin:10px 0 2px}
.foldbar button{font:inherit;font-size:12px;color:var(--muted);background:none;border:1px solid var(--line);
  border-radius:5px;padding:3px 10px;cursor:pointer}
.foldbar button:hover{color:var(--brand);border-color:var(--brand)}
.sub{font-size:11.5px;text-decoration:none;color:var(--ink2);background:var(--card);
  border:1px solid var(--line);border-radius:20px;padding:2px 10px}
.sub:hover{border-color:var(--jade);color:var(--jade)}
.sub.l3{opacity:.72;font-size:11px}
.body{margin-top:6px}
.body h3{font-family:var(--serif);font-size:19px;margin:30px 0 8px;padding-top:4px;scroll-margin-top:16px}
.body h4{font-size:15.5px;margin:22px 0 6px;color:var(--ink);scroll-margin-top:16px}
.body h5,.body h6{font-size:14px;margin:16px 0 5px}
.body p{margin:9px 0}
.body ul{margin:8px 0;padding-left:22px}
.body li{margin:4px 0}
.body li.task{list-style:none;margin-left:-20px;padding-left:26px;position:relative}
.cb{position:absolute;left:0;top:4px;width:16px;height:16px;border:1.5px solid var(--line2);
  border-radius:4px;font-size:11px;line-height:14px;text-align:center;color:#fff}
.task.done>.cb{background:var(--jade);border-color:var(--jade)}
.task.done{color:var(--mute)}
.task.done strong{font-weight:600;color:var(--mute)}
blockquote{margin:12px 0;padding:10px 14px;border-left:3px solid var(--jade);
  background:var(--jade-s);border-radius:0 8px 8px 0;font-size:13.5px}
code{font-family:var(--mono);font-size:12.5px;background:var(--card);
  border:1px solid var(--line);border-radius:4px;padding:1px 5px}
pre{background:var(--card);border:1px solid var(--line);border-radius:9px;
  padding:13px 15px;overflow-x:auto;margin:12px 0}
pre code{border:0;background:none;padding:0;font-size:12px;line-height:1.6}
.tw{overflow-x:auto;margin:14px 0;border:1px solid var(--line);border-radius:9px;background:var(--card)}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{padding:8px 12px;text-align:left;border-bottom:1px solid var(--line);vertical-align:top}
th{background:var(--jade-s);font-weight:600;font-size:12px;letter-spacing:.03em;white-space:nowrap}
tbody tr:last-child td{border-bottom:0}
td code{font-size:11.5px}
hr{border:0;border-top:1px solid var(--line);margin:26px 0}
a{color:var(--jade)}
.sev{display:inline-block;font-size:11px;font-weight:700;border-radius:5px;
  padding:1px 7px;letter-spacing:.03em;white-space:nowrap}
.sev-hi{background:var(--clay);color:#fff}
.sev-mid{background:var(--amber);color:#fff}
.sev-lo{background:var(--line);color:var(--ink2)}
.hidden{display:none}
.nores{margin:40px 0;color:var(--mute);font-size:14px}
@media print{
  aside{display:none} .wrap{display:block} main{max-width:none;padding:0}
  .doc{page-break-before:always} .subnav{display:none} .foldbar{display:none}
  .chap>summary::before{display:none} .cbody{display:block!important}
  body{font-size:11pt;background:#fff;color:#000}
}
@media (max-width:900px){
  .wrap{grid-template-columns:1fr} aside{position:static;height:auto}
  main{padding:24px 18px 60px}
}
</style>
</head>
<body>
<div class="wrap">
<aside>
  <div class="brand">TouchpadTapRight<small>觸控板輕觸右鍵 開發交接手冊</small></div>
  <div class="gen">產生於 __NOW__<br>由 <code>tools/產生交接網頁.py</code> 自 md 產生</div>
  <div class="stat"><b>__OPEN__</b><span>項未完成<br><small>完成者已移除，紀錄見工作狀態</small></span></div>
  <input id="q" type="search" placeholder="搜尋全部文件…（Ctrl+K）" autocomplete="off">
  <nav id="nav">__NAV__</nav>
</aside>
<main>
  <div class="hero">
    <h1>TouchpadTapRight 觸控板輕觸右鍵 開發交接</h1>
    <p>行動不便者的觸控板輔助工具｜C# .NET 9 WinForms＋Raw Input／HID API｜MIT 開源</p>
  </div>
  <div class="warn">
    <b>本頁為產生檔，請勿手改。</b>內容全部來自專案的 <code>*.md</code>；
    要修改請改對應的 md 檔，再重跑 <code>python tools/產生交接網頁.py</code>。
    本手冊引用的實機識別紀錄含機型與裝置 ID，屬技術資料，無個資。
  </div>
  __SECTIONS__
  <p class="nores hidden" id="nores">找不到符合的內容。</p>
</main>
</div>
<script>
(function(){
  var q=document.getElementById('q'),secs=[].slice.call(document.querySelectorAll('.doc'));
  var items=[].slice.call(document.querySelectorAll('.navitem')),nores=document.getElementById('nores');
  // 搜尋：整份文件過濾（區塊層級），保留可讀性
  function run(){
    var k=q.value.trim().toLowerCase();
    var any=false;
    secs.forEach(function(s,i){
      if(!k){s.classList.remove('hidden');[].forEach.call(s.querySelectorAll('.body>*'),function(e){e.classList.remove('hidden')});
        // 清空搜尋 → 章節回到「使用者自己點開的那些」（data-pin 記著）。
        // 注意是 open=有pin 而不是「沒pin才收」：搜尋期間被關掉的 pinned 章節要回來，
        // 不然讀到一半搜個東西再清空，剛剛展開的就消失了。
        [].forEach.call(s.querySelectorAll('.chap'),function(d){ d.open=d.hasAttribute('data-pin'); });
        any=true;return;}
      var hit=false;
      [].forEach.call(s.querySelectorAll('.body>*'),function(e){
        var m=e.textContent.toLowerCase().indexOf(k)>=0;
        e.classList.toggle('hidden',!m); if(m)hit=true;
      });
      // 命中在收合的章節裡 → 自動展開，否則搜尋等於瞎的
      [].forEach.call(s.querySelectorAll('.chap'),function(d){ d.open=d.textContent.toLowerCase().indexOf(k)>=0; });
      if(s.dataset.title.toLowerCase().indexOf(k)>=0){hit=true;[].forEach.call(s.querySelectorAll('.body>*'),function(e){e.classList.remove('hidden')});}
      s.classList.toggle('hidden',!hit); if(hit)any=true;
    });
    nores.classList.toggle('hidden',any);
  }
  q.addEventListener('input',run);

  // 手動點開的章節標記 data-pin：清空搜尋時不要把它收回去（讀到一半被收掉很惱人）
  [].forEach.call(document.querySelectorAll('.chap'),function(d){
    d.addEventListener('toggle',function(){
      if(!q.value.trim()){ if(d.open)d.setAttribute('data-pin','1'); else d.removeAttribute('data-pin'); }
    });
  });

  // 從側欄／子目錄跳過來時，目標章節要自動展開——否則點了連結卻停在一行收合的標題上，
  // 會以為連結壞了（2026-08-02 加摺疊時一併處理）。
  function openTarget(){
    if(!location.hash)return;
    var el=document.getElementById(location.hash.slice(1)); if(!el)return;
    var d=el.closest('.chap')||(el.tagName==='SUMMARY'?el.parentElement:null);
    if(d&&d.classList.contains('chap')){ d.open=true; d.setAttribute('data-pin','1'); }
    setTimeout(function(){ el.scrollIntoView({block:'start'}); },0);
  }
  window.addEventListener('hashchange',openTarget);
  openTarget();
  [].forEach.call(document.querySelectorAll('[data-fold]'),function(b){
    b.addEventListener('click',function(){
      var open=b.dataset.fold==='open', doc=b.closest('.doc');
      [].forEach.call(doc.querySelectorAll('.chap'),function(d){
        d.open=open; if(open)d.setAttribute('data-pin','1'); else d.removeAttribute('data-pin');
      });
    });
  });
  document.addEventListener('keydown',function(e){
    if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='k'){e.preventDefault();q.focus();q.select();}
    if(e.key==='Escape'&&document.activeElement===q){q.value='';run();q.blur();}
  });
  // 側欄高亮目前閱讀的文件
  var io=new IntersectionObserver(function(es){
    es.forEach(function(en){
      if(en.isIntersecting){
        items.forEach(function(a){a.classList.toggle('on',a.getAttribute('href')==='#'+en.target.id);});
      }
    });
  },{rootMargin:'-10% 0px -80% 0px'});
  secs.forEach(function(s){io.observe(s);});
})();
</script>
</body>
</html>"""

    out = (tpl.replace("__NOW__", now).replace("__OPEN__", str(tot_open))
              .replace("__NAV__", nav).replace("__SECTIONS__", "\n".join(sections)))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(out)
    print("已產生：%s（%.1f KB）" % (OUT, len(out.encode("utf-8")) / 1024))
    print("收錄 %d 份文件；尚有 %d 項未完成（另有 %d 項完成紀錄）" % (len(docs), tot_open, tot_done))
    return 0


if __name__ == "__main__":
    sys.exit(main())
