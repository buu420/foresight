"""Verify complete export file sets and build a searchable SQLite engine reference."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import sqlite3
from concurrent.futures import ThreadPoolExecutor

GAME_SHA256 = '8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7'


def source_row(task):
    folder, row = task
    source = folder / row['file']
    text = source.read_text(encoding='utf8')
    if not text.strip() or ('DECOMPILATION FAILED' in text) != (row['status'] == 'failed'):
        raise ValueError(f'Invalid function export: {source}')
    return row, source, text


def check_resource(task):
    root, row = task
    source = root / 'resources/raw' / row['path']
    raw = source.read_bytes()
    if len(raw) != int(row['decoded_bytes']) or hashlib.sha256(raw).hexdigest().upper() != row['sha256']:
        raise ValueError(f'Extracted resource mismatch: {source}')
    return len(raw)


def rows(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        yield from csv.DictReader(stream, delimiter='\t')


def build(root):
    db_path = root / 'engine.sqlite'
    db = sqlite3.connect(db_path)
    db.executescript('''
        DROP TABLE IF EXISTS documents;
        DROP TABLE IF EXISTS functions;
        DROP TABLE IF EXISTS calls;
        CREATE VIRTUAL TABLE documents USING fts5(kind, module, address, path UNINDEXED, name, content);
        CREATE TABLE functions(module TEXT, rva TEXT, name TEXT, bytes INTEGER, status TEXT, path TEXT,
                               PRIMARY KEY(module,rva));
        CREATE TABLE calls(module TEXT, caller TEXT, callee TEXT, name TEXT);
        CREATE INDEX calls_caller ON calls(module,caller);
        CREATE INDEX calls_callee ON calls(module,callee);
    ''')
    inventory = json.loads((root / 'libraries/native-libraries.json').read_text(encoding='utf-8-sig'))
    modules = [('Chrono Trigger.exe', root / 'native', GAME_SHA256)]
    modules += [(m['name'], root / 'libraries' / m['name'], m['sha256']) for m in inventory]
    report = dict(modules=[], resources={}, textDocuments=0)
    for name, folder, expected in modules:
        manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf8'))
        if not manifest['exportComplete'] or manifest['executableSha256'] != expected:
            raise ValueError(f'Incomplete or mismatched module: {name}')
        if hashlib.sha256((root / 'binaries' / name).read_bytes()).hexdigest().upper() != expected:
            raise ValueError(f'Preserved binary hash mismatch: {name}')
        count, successful = 0, 0
        with ThreadPoolExecutor(max_workers=16) as pool:
            sources = list(pool.map(source_row, ((folder, row) for row in rows(folder / 'functions.tsv'))))
        for row, source, text in sources:
            relative = source.relative_to(root).as_posix()
            db.execute('INSERT INTO functions VALUES (?,?,?,?,?,?)', (name, row['rva'], row['name'], int(row['bytes']), row['status'], relative))
            db.execute('INSERT INTO documents VALUES (?,?,?,?,?,?)', ('native', name, row['rva'], relative, row['name'], text))
            count += 1
            successful += row['status'] == 'decompiled'
        if count != manifest['functions'] - manifest['external'] or successful != manifest['decompiled']:
            raise ValueError(f'Function counts do not match: {name}')
        if len(list((folder / 'functions').glob('*.c'))) != count:
            raise ValueError(f'Unexpected extra/missing function files: {name}')
        with (folder / 'game.c').open(encoding='utf8') as combined:
            combined_ids = [line.split(';', 1)[0].removeprefix('/* RVA 0x') for line in combined if line.startswith('/* RVA 0x')]
        if len(combined_ids) != count or len(set(combined_ids)) != count or set(combined_ids) != {r['rva'] for r, _, _ in sources}:
            raise ValueError(f'Combined C export incomplete or duplicated: {name}')
        for call in rows(folder / 'calls.tsv'):
            db.execute('INSERT INTO calls VALUES (?,?,?,?)', (name, call['from_rva'], call['to_rva'], call['to_name']))
        for row in rows(folder / 'symbols.tsv'):
            db.execute('INSERT INTO documents VALUES (?,?,?,?,?,?)', ('symbol', name, row['address_rva'],
                (folder / 'symbols.tsv').relative_to(root).as_posix(), row['name'], row['name']))
        for row in rows(folder / 'defined-data.tsv'):
            db.execute('INSERT INTO documents VALUES (?,?,?,?,?,?)', ('data', name, row['address_rva'],
                (folder / 'defined-data.tsv').relative_to(root).as_posix(), row['type'], row['value']))
        instruction_count = sum(1 for _ in (folder / 'game.asm').open(encoding='utf8')) - 1
        if instruction_count != manifest['instructions']:
            raise ValueError(f'Assembly count does not match: {name}')
        recovered_count = 0
        recovered_ok = 0
        recovery = folder / 'recovered'
        if (recovery / 'manifest.json').exists():
            recovery_manifest = json.loads((recovery / 'manifest.json').read_text(encoding='utf8'))
            if not recovery_manifest['exportComplete'] or recovery_manifest.get('executableSha256', expected) != expected:
                raise ValueError(f'Incomplete function discovery recovery: {name}')
            with ThreadPoolExecutor(max_workers=16) as pool:
                recovered_sources = list(pool.map(source_row, ((recovery, row) for row in rows(recovery / 'functions.tsv'))))
            for row, source, text in recovered_sources:
                relative = source.relative_to(root).as_posix()
                db.execute('INSERT INTO functions VALUES (?,?,?,?,?,?)', (name, row['rva'], row['name'], int(row['bytes']), row['status'], relative))
                db.execute('INSERT INTO documents VALUES (?,?,?,?,?,?)', ('recovered-native', name, row['rva'], relative, row['name'], text))
                recovered_count += 1
                recovered_ok += row['status'] == 'decompiled'
            if recovered_count != recovery_manifest['functions'] or recovered_ok != recovery_manifest['decompiled']:
                raise ValueError(f'Recovered function counts mismatch: {name}')
            if len(list((recovery / 'functions').glob('*.c'))) != recovered_count:
                raise ValueError(f'Unexpected recovered function files: {name}')
            for call in rows(recovery / 'calls.tsv'):
                db.execute('INSERT INTO calls VALUES (?,?,?,?)', (name, call['from_rva'], call['to_rva'], call['to_name']))
        report['modules'].append(dict(name=name, **manifest, recoveredFunctionCandidates=recovered_count,
                                     recoveredDecompiled=recovered_ok))
        db.commit()
        print(f'Indexed {name}: {count} functions', flush=True)
    resource_manifest = json.loads((root / 'resources/manifest.json').read_text(encoding='utf8'))
    if not resource_manifest['exportComplete'] or resource_manifest['executableSha256'] != GAME_SHA256:
        raise ValueError('Resource export incomplete or mismatched')
    with ThreadPoolExecutor(max_workers=16) as pool:
        lengths = list(pool.map(check_resource, ((root, row) for row in rows(root / 'resources/resources.tsv'))))
    total, count = sum(lengths), len(lengths)
    if count != resource_manifest['resourceCount'] or total != resource_manifest['decodedBytes']:
        raise ValueError('Resource manifest mismatch')
    report['resources'] = {k: v for k, v in resource_manifest.items() if k != 'scripts'}
    report['resources']['fieldDecodedCodeBytes'] = sum(s.get('decodedBytes', 0) for s in resource_manifest['scripts'])
    report['resources']['fieldTotalCodeBytes'] = sum(s.get('codeBytes', 0) for s in resource_manifest['scripts'])
    world_manifest = json.loads((root / 'resources/world-scripts/manifest.json').read_text(encoding='utf8'))
    if len(world_manifest['scripts']) != resource_manifest['worldScripts']:
        raise ValueError('World script count mismatch')
    for script in world_manifest['scripts']:
        if script['status'] != 'decoded' or script['decodedBytes'] != script['bytes']:
            raise ValueError(f'World script incomplete: {script["path"]}')
    report['resources']['worldScriptsDecoded'] = len(world_manifest['scripts'])
    if len(list((root / 'resources/field-scripts').glob('*.txt'))) != resource_manifest['fieldScripts']:
        raise ValueError('Field script listing count mismatch')
    for folder in ('resources/field-scripts', 'resources/world-scripts', 'resources/raw/Localize/en/msg'):
        for source in (root / folder).glob('*.txt'):
            relative = source.relative_to(root).as_posix()
            db.execute('INSERT INTO documents VALUES (?,?,?,?,?,?)', ('script' if 'scripts' in folder else 'text', '', '', relative, source.name, source.read_text(encoding='utf-8-sig')))
            report['textDocuments'] += 1
    db.commit()
    db.close()
    report.update(verified=True, totalFunctions=sum(m['functions'] + m['recoveredFunctionCandidates'] for m in report['modules']),
                  decompiledFunctions=sum(m['decompiled'] + m['recoveredDecompiled'] for m in report['modules']),
                  remainingFailures=sum(m['failed'] + m['recoveredFunctionCandidates'] - m['recoveredDecompiled'] for m in report['modules']))
    (root / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf8')
    print(json.dumps({k: v for k, v in report.items() if k not in ('modules', 'resources')}, indent=2))


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('root', type=Path)
    a = p.parse_args()
    build(a.root.resolve())
