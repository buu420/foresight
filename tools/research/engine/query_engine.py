"""Search the full exported engine or inspect a function's callers and callees."""
import argparse
from pathlib import Path
import sqlite3

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--root', type=Path, required=True)
p.add_argument('--module', default='Chrono Trigger.exe')
p.add_argument('--rva', help='Function entry RVA in hexadecimal')
p.add_argument('--search', help='SQLite FTS5 query; quote phrases')
p.add_argument('--limit', type=int, default=12)
a = p.parse_args()
database = (a.root / 'engine.sqlite').absolute()
if not database.is_file():
    raise SystemExit(f'Engine index does not exist: {database}')
# SQLite interprets a UNC URI host as an authority and rejects it on Windows.
# Open the native path directly and enforce read-only statements on this connection.
db = sqlite3.connect(database)
db.execute('PRAGMA query_only = ON')
if a.rva:
    rva = f'{int(a.rva, 16):08X}'
    found = db.execute('SELECT name,status,path FROM functions WHERE module=? AND rva=?', (a.module, rva)).fetchone()
    if found:
        print(f'{a.module} RVA {rva}: {found[0]} ({found[1]})\n{(a.root / found[2]).resolve()}')
        print('Callers:', ', '.join(r[0] for r in db.execute('SELECT caller FROM calls WHERE module=? AND callee=?', (a.module, rva))))
        print('Callees:', ', '.join(f'{r[0]} {r[1]}' for r in db.execute('SELECT callee,name FROM calls WHERE module=? AND caller=?', (a.module, rva))))
    else:
        raise SystemExit('No function starts at that RVA in the selected module.')
if a.search:
    for row in db.execute('SELECT kind,module,address,path,name,snippet(documents,5,"[", "]", "...",24) FROM documents WHERE documents MATCH ? ORDER BY rank LIMIT ?', (a.search, max(1,min(a.limit,100)))):
        print(f'{row[0]} {row[1]} {row[2]} {row[4]}\n{(a.root / row[3]).resolve()}\n{row[5]}\n')
db.close()
