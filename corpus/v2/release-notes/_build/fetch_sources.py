"""Fetches the pinned changelog sources for the release-notes project (dev tool).  Needs the GitHub CLI (gh), signed in.

Writes kong.md, kong-old.md, kong-LICENSE.txt, dates.json and kall.json into the scratch folder (CORPUS_SCRATCH or ./_scratch).
Run once before rn_make.py.
"""
import json, os, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from corplib import SP
import kparse

REPO = 'Kong/kong'
SHA = '21032122ab80c973f368f6bc539f53985e7164e8'
TAGS = '3.9.1 3.9.0 3.8.0 3.7.0 3.6.0 3.5.0 3.4.0 3.3.0 3.2.0 3.1.0 2.8.0 2.7.0 2.5.0'.split()


def gh(*args):
    r = subprocess.run(['gh', 'api'] + list(args), capture_output=True, text=True, encoding='utf-8')
    if r.returncode: raise SystemExit(r.stderr)
    return r.stdout


for path, out in [('CHANGELOG.md', 'kong.md'), ('CHANGELOG-OLD.md', 'kong-old.md'), ('LICENSE', 'kong-LICENSE.txt')]:
    text = gh('-H', 'Accept: application/vnd.github.raw', 'repos/%s/contents/%s?ref=%s' % (REPO, path, SHA))
    open(os.path.join(SP, out), 'w', encoding='utf-8', newline='\n').write(text)
dates = {}
for v in TAGS:
    dates[v] = gh('repos/%s/releases/tags/%s' % (REPO, v), '--jq', '.published_at').strip()[:10]
json.dump(dates, open(os.path.join(SP, 'dates.json'), 'w'))
allr = kparse.parse(os.path.join(SP, 'kong.md')) + kparse.parse(os.path.join(SP, 'kong-old.md'))
json.dump(allr, open(os.path.join(SP, 'kall.json'), 'w'))
print('fetched', len(allr), 'releases from', REPO, SHA)
