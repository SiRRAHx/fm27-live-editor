# Renders the app's default "carbon" background (styles.css .bg-carbon) into menubg.png,
# the texture behind the Live Editor button on FM's main menu. Run again if the look changes.
import math, struct, zlib
W, H, S = 360, 88, 2.0          # about twice the button's size on screen; S = CSS px scale
ALPHA = 0.80                     # how solid the button is (was 0.92 as a flat colour)
base1, bg, base3 = (0x0b, 0x23, 0x1a), (0x06, 0x14, 0x10), (0x04, 0x0d, 0x0a)
accent = (52, 211, 153)
def mix(a, b, t): return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))
def over(dst, src, a): return tuple(dst[i] * (1 - a) + src[i] * a for i in range(3))
gx, gy = math.sin(math.radians(160)), -math.cos(math.radians(160))
span = abs(W * gx) + abs(H * gy)
rows = []
for y in range(H):
    row = bytearray([0])
    for x in range(W):
        t = ((x - W / 2) * gx + (y - H / 2) * gy) / span + 0.5
        c = mix(base1, bg, t / 0.6) if t < 0.6 else mix(bg, base3, (t - 0.6) / 0.4)
        u, v = x / S, y / S
        if (0.7071 * u - 0.7071 * v) % 8 < 3: c = over(c, (0, 0, 0), 0.32)
        if (-0.7071 * u - 0.7071 * v) % 8 < 3: c = over(c, (255, 255, 255), 0.028)
        dx, dy = (x / W - 0.2) / 0.7, (y / H + 0.1) / 0.55
        g = max(0.0, 1 - math.hypot(dx, dy) / 0.7) * 0.16
        c = over(c, accent, g)
        row += bytes(int(round(min(255, max(0, k)))) for k in c) + bytes([int(ALPHA * 255)])
    rows.append(bytes(row))
def chunk(t, b): return struct.pack('>I', len(b)) + t + b + struct.pack('>I', zlib.crc32(t + b) & 0xffffffff)
png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', W, H, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b''.join(rows), 9)) + chunk(b'IEND', b'')
open('menubg.png', 'wb').write(png)
print('menubg.png', len(png), 'bytes')
