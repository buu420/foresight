"""Compare action availability, not row order/count, with the optional z3-solver.

Run against an old and candidate catalog. The solver dependency is research-only;
it is never included in the mod. A difference is evidence to inspect, not proof
that either version is correct. Region rectangles are part of their condition.
"""
import argparse
import json
from pathlib import Path
import z3


def grouped(catalog):
    result = {}
    for scene in catalog['Scenes']:
        for actor in scene['Actors']:
            for row in actor['Actions']:
                key = (scene['Id'], actor['Id'], 'actor', json.dumps(
                    {k: v for k, v in row.items() if k != 'Guards'}, sort_keys=True))
                result.setdefault(key, []).append(row)
        for row in scene['Regions']:
            key = (scene['Id'], row['Actor'], 'region', json.dumps(
                {k: v for k, v in row.items() if k not in ('Guards', 'Left', 'Top', 'Right', 'Bottom')}, sort_keys=True))
            result.setdefault(key, []).append(row)
    return result


def compare(old, new):
    before, after = grouped(old), grouped(new)
    changes = []
    for key in sorted(before.keys() | after.keys()):
        variables = {}
        def variable(source, index):
            return variables.setdefault((source, index), z3.BitVec(f'{source}_{index}', 32))
        def clause(row):
            conditions = []
            for g in row['Guards']:
                v, r, op = variable(g['Source'], g['Index']), g['Value'], g['Operation']
                predicate = (v == r, v != r, v > r, v < r, v >= r, v <= r,
                             (v & r) != 0, (v | r) != 0)[op]
                conditions.append(predicate if g['Expected'] else z3.Not(predicate))
            if key[2] == 'region':
                x, y = variable('X', 0), variable('Y', 0)
                conditions += [x >= row['Left'], x <= row['Right'], y >= row['Top'], y <= row['Bottom']]
            return z3.And(conditions)
        a = z3.Or([clause(r) for r in before.get(key, [])])
        b = z3.Or([clause(r) for r in after.get(key, [])])
        solver = z3.Solver()
        solver.set(timeout=10000)
        for (source, _), value in variables.items():
            maximum = 1 if source in ('Recruited', 'ActiveParty') else 99 if source == 'Item' else \
                255 if source in ('Global', 'Local', 'X', 'Y') else 65535 if source == 'Extra' else 2147483647
            solver.add(value >= 0, value <= maximum)
        row = dict(Scene=key[0], Actor=key[1], Surface=key[2], Action=json.loads(key[3]))
        for name, condition in [('Removed', z3.And(a, z3.Not(b))), ('Added', z3.And(b, z3.Not(a)))]:
            solver.push(); solver.add(condition)
            verdict = solver.check()
            if verdict != z3.unsat:
                row[name] = {str(v): solver.model().eval(v, model_completion=True).as_long() for v in variables.values()} \
                    if verdict == z3.sat else 'Solver timeout'
            solver.pop()
        if 'Added' in row or 'Removed' in row:
            changes.append(row)
    return dict(Compared=len(before.keys() | after.keys()), Changes=changes,
                OldWarnings=old['ExtractionWarnings'], NewWarnings=new['ExtractionWarnings'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('old', type=Path)
    parser.add_argument('new', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    report = compare(json.loads(args.old.read_text()), json.loads(args.new.read_text()))
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(dict(Compared=report['Compared'], Changes=len(report['Changes']),
                         Removed=sum('Removed' in r for r in report['Changes']),
                         Added=sum('Added' in r for r in report['Changes']))))
