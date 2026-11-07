from pathlib import Path

p = Path(__file__).resolve().parents[1] / "website" / "index.html"
t = p.read_text(encoding="utf-8")
repl = {
    "\u2014": "&mdash;",
    "\u2013": "&ndash;",
    "\u00b7": "&middot;",
    "\u2192": "&rarr;",
    "\u2026": "&hellip;",
    "\u00a9": "&copy;",
    "\u2019": "'",
    "\u2018": "'",
}
for a, b in repl.items():
    t = t.replace(a, b)
if 'charset="utf-8"' in t and "Content-Type" not in t:
    t = t.replace(
        '<meta charset="utf-8" />',
        '<meta charset="utf-8" />\n  <meta http-equiv="Content-Type" content="text/html; charset=utf-8" />',
        1,
    )
p.write_text(t, encoding="ascii", errors="strict")
print("fixed", p)
