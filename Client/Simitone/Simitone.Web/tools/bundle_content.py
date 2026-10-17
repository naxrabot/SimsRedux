"""
SimsRedux: packs the website's shipped mods into a few compressed bundles.

GitHub Pages sends game files as they are (it only compresses text), so the custom content
(Content/redux/mods: objects, walls, floors, skins) would download at full size. This puts those
files into gzip bundles instead - about half the size - each named after what's in it, so a
browser that already has a bundle keeps using it (js/simsfs.js caches them) until it changes.

Bundle format (the same as the server's /api/pack): for each file
  [u16 path length][path, UTF-8, relative to Content/][u32 size][bytes]

usage: python bundle_content.py <site>/Content
Rewrites <site>/Content/files.txt: the bundled files come out of it, the bundles go in.
"""
import gzip, hashlib, os, struct, sys

content = sys.argv[1]
mods = os.path.join(content, 'redux', 'mods')
if not os.path.isdir(mods):
    print('SimsRedux: no redux/mods folder - nothing to bundle'); sys.exit(0)

files = []
for dp, dn, fn in os.walk(mods):
    for f in fn:
        full = os.path.join(dp, f)
        rel = os.path.relpath(full, content).replace('\\', '/')
        files.append((rel, full))
files.sort()

CHUNK = 48 * 1024 * 1024  # raw bytes per bundle (each comes out well under GitHub's 100 MB file limit)
bundles, cur, size = [], [], 0
for rel, full in files:
    s = os.path.getsize(full)
    if cur and size + s > CHUNK:
        bundles.append(cur); cur, size = [], 0
    cur.append((rel, full)); size += s
if cur: bundles.append(cur)

names = []
raw_total = gz_total = 0
for i, group in enumerate(bundles):
    data = bytearray()
    for rel, full in group:
        b = open(full, 'rb').read()
        p = rel.encode('utf-8')
        data += struct.pack('<H', len(p)) + p + struct.pack('<I', len(b)) + b
    digest = hashlib.sha1(data).hexdigest()[:10]
    name = 'redux/mods-%d-%s.srxpack.gz' % (i + 1, digest)
    gz = gzip.compress(bytes(data), compresslevel=9, mtime=0)
    with open(os.path.join(content, name), 'wb') as f: f.write(gz)
    names.append(name)
    raw_total += len(data); gz_total += len(gz)

for rel, full in files: os.remove(full)
for dp, dn, fn in os.walk(mods, topdown=False):
    if not os.listdir(dp): os.rmdir(dp)

lst = os.path.join(content, 'files.txt')
bundled = set(rel for rel, full in files)
lines = [l.strip() for l in open(lst, encoding='utf-8-sig').read().splitlines() if l.strip()]
lines = [l for l in lines if l.replace('\\', '/') not in bundled]
lines += names
with open(lst, 'w', encoding='utf-8', newline='\n') as f: f.write('\n'.join(lines) + '\n')
print('SimsRedux: bundled %d mod files: %.1f MB -> %.1f MB in %d bundles' % (len(files), raw_total / 1e6, gz_total / 1e6, len(names)))
