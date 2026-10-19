"""Sharp full-page screenshot of a local HTML file or a loopback page with headless Chrome (dev-only).
  python .claude\\tools\\shot.py <file.html | http://127.0.0.1:port/...> <out.png> [--width 1400] [--scale 2]
The page is rendered at --scale device pixels per CSS pixel, then the blank space below the content is trimmed.
"""
import argparse, os, subprocess, tempfile
from PIL import Image, ImageChops

CHROME = r'C:\Program Files\Google\Chrome\Application\chrome.exe'

ap = argparse.ArgumentParser()
ap.add_argument('src'); ap.add_argument('out')
ap.add_argument('--width', type=int, default=1400); ap.add_argument('--height', type=int, default=6000)
ap.add_argument('--scale', type=float, default=2)
a = ap.parse_args()
url = a.src if a.src.startswith('http') else 'file:///' + os.path.abspath(a.src).replace('\\', '/')
prof = tempfile.mkdtemp(prefix='shot-')
subprocess.run([CHROME, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', '--user-data-dir=' + prof,
                '--force-device-scale-factor=' + str(a.scale), '--window-size=%d,%d' % (a.width, a.height),
                '--virtual-time-budget=3000', '--screenshot=' + os.path.abspath(a.out), url], check=True, capture_output=True, timeout=120)
im = Image.open(a.out).convert('RGB')
bg = Image.new('RGB', im.size, im.getpixel((im.width - 1, im.height - 1)))
box = ImageChops.difference(im, bg).getbbox()
if box: im = im.crop((0, 0, im.width, min(im.height, box[3] + int(40 * a.scale))))
im.save(a.out, optimize=True)
print(a.out, im.size)
