import sys, subprocess
import os
D = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, D)
import legacy_build as LB
import legacy_label as LL
from corplib import *

steps = sys.argv[1:] or ['gen', 'passA', 'inject', 'ctx', 'passB', 'ctx', 'enc', 'check', 'validate']
for s in steps:
    print('=== ' + s)
    if s == 'gen':
        tp = LB.generate(); LB.project_files(tp)
    elif s == 'passA': LL.pass_a()
    elif s == 'inject': LL.inject()
    elif s == 'ctx': kit('contexts', '--project', LL.OUT, '--update')
    elif s == 'passB': LL.pass_b()
    elif s == 'enc': LL.encodings()
    elif s == 'check': kit('check', '--project', LL.OUT)
    elif s == 'validate':
        r = subprocess.run([SCORING, 'validate', '--labels', LL.OUT + '/labels.jsonl', '--kinds', WT + '/corpus/schema/defects.yaml', '--root', LL.OUT], capture_output=True, text=True, encoding='utf-8')
        print(r.stdout[-3000:], r.stderr[-1000:])
