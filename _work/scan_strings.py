import re, sys

path = sys.argv[1]
data = open(path, 'rb').read()

pat16 = re.compile(rb'(?:[\x20-\x7e]\x00){6,}')
hits16 = [m.group().decode('utf-16le') for m in pat16.finditer(data)]
pat8 = re.compile(rb'[\x20-\x7e]{8,}')
hits8 = [m.group().decode('ascii', 'ignore') for m in pat8.finditer(data)]

keywords = [k.lower() for k in sys.argv[2].split(',')]
seen = set()
out = []
for s in hits16 + hits8:
    low = s.lower()
    if any(k in low for k in keywords) and s not in seen:
        seen.add(s)
        out.append(s)
print('\n'.join(out))
