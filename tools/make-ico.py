from pathlib import Path

from PIL import Image

src = Path(__file__).resolve().parents[1] / "src" / "AutoCorrect.App" / "Assets" / "autocorrect-icon.jpg"
dst = src.with_name("autocorrect.ico")
img = Image.open(src).convert("RGBA")
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
img.save(dst, format="ICO", sizes=sizes)
print(f"wrote {dst} ({dst.stat().st_size} bytes)")
