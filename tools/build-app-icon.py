from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets" / "app-icon.ico"
SIZE = 1024


def mix(a, b, amount):
    return tuple(round(a[index] * (1 - amount) + b[index] * amount) for index in range(3))


image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
pixels = image.load()
top = (119, 108, 255)
bottom = (37, 183, 211)

for y in range(SIZE):
    for x in range(SIZE):
        progress = min(1, max(0, (x * 0.36 + y * 0.64) / SIZE))
        color = mix(top, bottom, progress)
        glow = max(0, 1 - (((x - 235) ** 2 + (y - 170) ** 2) ** 0.5) / 760)
        color = mix(color, (255, 255, 255), glow * 0.16)
        pixels[x, y] = (*color, 255)

mask = Image.new("L", (SIZE, SIZE), 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), radius=238, fill=255)
image.putalpha(mask)

draw = ImageDraw.Draw(image)
scale = SIZE / 256


def point(x, y):
    return (round(x * scale), round(y * scale))


def width(value):
    return round(value * scale)


white = (255, 255, 255, 248)
soft_white = (255, 255, 255, 218)
segments = [
    (point(78, 91), point(128, 60)),
    (point(128, 60), point(178, 91)),
    (point(78, 91), point(78, 165)),
    (point(178, 91), point(178, 165)),
    (point(78, 165), point(128, 196)),
    (point(128, 196), point(178, 165)),
    (point(128, 79), point(89, 151)),
    (point(128, 79), point(167, 151)),
    (point(96, 165), point(160, 165)),
]

for start, end in segments:
    draw.line((start, end), fill=soft_white, width=width(13))

for center in (point(128, 60), point(78, 165), point(178, 165)):
    radius = width(20)
    draw.ellipse((center[0] - radius, center[1] - radius, center[0] + radius, center[1] + radius), fill=white)

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
image.save(OUTPUT, format="ICO", sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])
print(OUTPUT)
