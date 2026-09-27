"""Extract bounded, native script paths to interactions and location changes.

This is a read-only metadata compiler, not an interpreter running in the game.
Only immediate comparisons audited in the PC handlers become availability gates.
Input choices are possible user actions, never fabricated current input values.
"""
from decode_verified import actor_ops, instruction, walk, CONDITIONAL
import heapq
import itertools


def compare(value, rhs, op):
    return (value == rhs, value != rhs, value > rhs, value < rhs,
            value >= rhs, value <= rhs, bool(value & rhs), bool(value | rhs))[op]


def guard(source, index, operation, value, expected=True):
    return dict(Source=source, Index=index, Operation=operation, Value=value, Expected=expected)


def intervals(values):
    result = []
    for value in sorted(values):
        if result and result[-1][1] + 1 == value:
            result[-1][1] = value
        else:
            result.append([value, value])
    return result


def joined_guards(clauses, requirements):
    """Admit a new path at an identical program state, merging proven joins.

    A weaker clause covers a stronger one. Two clauses differing only in one
    predicate's truth value cover their shared requirements. The caller keys
    this set by PC, return stack and substitutions, so paths with different
    effects or callers cannot be merged. No unrelated conditions are dropped.
    """
    clause = frozenset(tuple(g.values()) for g in requirements)
    while True:
        if any(prior <= clause for prior in clauses):
            return None
        merged = False
        for prior in sorted(clauses, key=lambda c: sorted(c)):
            left, right = clause - prior, prior - clause
            if len(left) == len(right) == 1:
                a, b = next(iter(left)), next(iter(right))
                if a[:-1] == b[:-1] and a[-1] != b[-1]:
                    clause &= prior
                    merged = True
                    break
        if not merged:
            break
    clauses.difference_update(prior for prior in tuple(clauses) if clause < prior)
    clauses.add(clause)
    return tuple(guard(*g) for g in sorted(clause))


def simplify(records):
    """Eliminate a condition only when both outcomes reach the SAME action."""
    import json
    grouped = {}
    for record in records:
        identity = {k: v for k, v in record.items() if k != 'Guards'}
        key = json.dumps(identity, sort_keys=True)
        clauses = grouped.setdefault(key, (identity, set()))[1]
        clauses.add(frozenset(tuple(g.values()) for g in record['Guards']))
    output = []
    for identity, clauses in grouped.values():
        changed = True
        while changed:
            changed = False
            # Greedy absorption is equivalent in any order, but hash iteration
            # can choose different covers and change generated region identities.
            for first in sorted(clauses, key=lambda c: sorted(c)):
                for second in sorted(clauses, key=lambda c: sorted(c)):
                    if first < second:
                        clauses.discard(second)
                        changed = True
                        continue
                    a, b = first - second, second - first
                    if len(a) == len(b) == 1:
                        x, y = next(iter(a)), next(iter(b))
                        if x[:-1] == y[:-1] and x[-1] != y[-1]:
                            clauses.discard(first)
                            clauses.discard(second)
                            clauses.add(first & second)
                            changed = True
        for clause in sorted(clauses, key=lambda c: sorted(c)):
            output.append(dict(identity, Guards=[guard(*g) for g in sorted(clause)]))
    return output


def coordinate_locals(code, actors):
    """Shared cells whose only native producer reads the leader's coordinates.

    These can be maintained by a separate room controller (the End of Time
    pillars do that). Scratch cells with any other writer are not global aliases.
    """
    candidates = {}
    overwritten = set()
    for functions in actors:
        for op, a in actor_ops(code, functions).values():
            if a is None:
                continue
            if op == 0x22 and a[0] == 0:
                candidates.setdefault(a[1], set()).add('X')
                candidates.setdefault(a[2], set()).add('Y')
            elif op in (0x21, 0x22):
                overwritten.update(a[1:3])
            elif op in (0x75, 0x76, 0x77, 0x20, 0x55, 0x71, 0x72, 0x73):
                overwritten.add(a[0])
            elif op in (0x4f, 0x50, 0x48, 0x49, 0x51, 0x52, 0x53, 0x54, 0x3e, 0x74, 0x70,
                        0x5b, 0x5d, 0x5e, 0x5f, 0x60, 0x61, 0x63, 0x64, 0x67, 0x69, 0x6b):
                overwritten.add(a[-1])
    return {key: next(iter(axes)) for key, axes in candidates.items() if len(axes) == 1 and key not in overwritten}


def condition_variables(code, actors, start):
    """Cells which can affect a gate, including every ordinary actor request.

    Stores to other cells remain real game effects; they cannot affect this
    compiler's emitted conditions. Excluding them from its abstract state avoids
    enumerating combinations of equipment/restoration writes with no gate reads.
    This is deliberately conservative across loops, overwrites and return paths.
    """
    pending, seen, variables = [start], set(), set()
    while pending:
        entry = pending.pop()
        if entry in seen:
            continue
        seen.add(entry)
        for op, a in walk(code, [entry]).values():
            if a is None:
                continue
            if op in (2, 3, 4) and a[0] // 2 < len(actors):
                pending.append(actors[a[0] // 2][a[1] & 15])
            if op == 0x18:
                variables.add(('Global', 0))
            elif op in (0x12, 0x16, 0x6e):
                source = 'Local' if op == 0x12 else 'Global' if op == 0x16 else 'Extra'
                variables.add((source, a[0] + (256 if op == 0x16 and a[2] & 128 else 0)))
            elif op == 0xc9:
                variables.add(('ItemsChanged', 0))
            elif op == 0xcc:
                variables.add(('Gold', 0))
            elif op in (0xcf, 0xd2):
                variables.add(('PartyChanged', 0))
    return variables


def paths(code, actors, start, coordinate_variables=None, budget=120000, diagnostic=None, initial_coordinates=None,
          signal_encounters=()):
    # Work entries: pc, active call frames, requirements, substitutions.
    # Each frame is (resume PC, callee entry); a completed visit is not recursion.
    initial = {('Local', key): axis for key, axis in (coordinate_variables or initial_coordinates or {}).items()}
    read_variables = condition_variables(code, actors, start)
    read_variables.update((g['Source'], g['Index']) for action in signal_encounters for g in action['Guards'])
    # Process earlier branch destinations before pushing their common suffix
    # through the rest of the script. A depth-first stack exhausts every suffix
    # for each stronger history before their common join can absorb them.
    pending = []
    sequence = itertools.count()
    def enqueue(item):
        heapq.heappush(pending, (item[0], len(item[2]), next(sequence), item))
    enqueue((start, (), (), initial))
    output = []
    work = 0
    seen = {}
    effects = {}
    writes = {}
    def local_writes(entry, visiting=frozenset()):
        if entry in writes:
            return writes[entry]
        if entry in visiting:
            return {key for key in read_variables if key[0] == 'Local'}
        found = set()
        for op, a in walk(code, [entry]).values():
            if a is None or 5 <= op <= 7:
                found.update(key for key in read_variables if key[0] == 'Local')
            elif op in (2, 3, 4) and a[0] // 2 < len(actors):
                found.update(local_writes(actors[a[0] // 2][a[1] & 15], visiting | {entry}))
            elif op in (0x21, 0x22):
                found.update(('Local', index) for index in a[1:3])
            elif op in (0x75, 0x76, 0x77, 0x20, 0x55, 0x71, 0x72, 0x73):
                found.add(('Local', a[0]))
            elif op in (0x4f, 0x50, 0x48, 0x49, 0x51, 0x52, 0x53, 0x54, 0x3e, 0x74, 0x70,
                        0x5b, 0x5d, 0x5e, 0x5f, 0x60, 0x61, 0x63, 0x64, 0x67, 0x69, 0x6b):
                found.add(('Local', a[-1]))
        writes[entry] = found
        return found
    def effectful(entry, visiting=frozenset()):
        if entry in effects:
            return effects[entry]
        if entry in visiting:
            return True
        ops = walk(code, [entry])
        relevant = {0x3a, 0x45, 0x46, 0x56, 0x5a, 0x65, 0x66, 0xbb, 0xc0, 0xc1, 0xc2, 0xc3, 0xc4,
                    0xc7, 0xc8, 0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xd0, 0xd1, 0xd3, 0xd4, 0xd6, 0xd8, 0xe4, 0xe5, *range(0xdc, 0xe2)}
        if signal_encounters:
            relevant.update((0x4f, 0x50, 0x75, 0x76, 0x77))
        value = any(a is None or op in relevant for op, a in ops.values())
        if not value:
            value = any(effectful(actors[a[0] // 2][a[1] & 15], visiting | {entry})
                        for op, a in ops.values() if op in (2, 3, 4) and a[0] // 2 < len(actors))
        effects[entry] = value
        return value
    while pending:
        pc, returns, guards, values = heapq.heappop(pending)[3]
        while 0 <= pc < len(code):
            values = {key: value for key, value in values.items() if key in read_variables}
            state = (pc, returns, tuple(sorted(values.items())))
            joined = joined_guards(seen.setdefault(state, set()), guards)
            if joined is None:
                break
            guards = joined
            work += 1
            if work > budget:
                # An incomplete expansion must never masquerade as audited data.
                if diagnostic is not None:
                    from collections import Counter
                    common = Counter(key[0] for key in seen).most_common(5)
                    diagnostic.append(dict(Reason='budget', Work=work, Pc=pc,
                        ProgramStates=len(seen), CommonLocations=common,
                        StateSamples=[dict(Pc=k[0], Returns=k[1], Values=k[2], Clauses=len(v))
                                      for k, v in seen.items() if k[0] == common[0][0]][:8]))
                return [], False
            op, a = instruction(code, pc)
            if a is None:
                if diagnostic is not None:
                    diagnostic.append(dict(Reason='instruction', Work=work, Pc=pc, Opcode=op))
                return [], False
            next_pc = pc + 1 + len(a)
            if op in (0, 0xb2):
                if returns:
                    # Actor requests are scheduled concurrently. Completion may
                    # change shared locals through another actor's callback.
                    values = {k: None if k[0] == 'Local' else v for k, v in values.items()}
                    pc = returns[-1][0]
                    returns = returns[:-1]
                    continue
                break
            if op in (0x10, 0x11):
                pc += 1 + (a[0] if op == 0x10 else -a[0])
                continue
            if op in (2, 3, 4) and a[0] // 2 < len(actors) and len(returns) < 4:
                # These entry points may animate or perform the actual activation.
                target = actors[a[0] // 2][a[1] & 15]
                if effectful(target) and target != start and all(frame[1] != target for frame in returns):
                    returns += ((next_pc, target),)
                    pc = target
                    continue
            if 2 <= op <= 7:
                values = {k: None if k[0] == 'Local' else v for k, v in values.items()}
                changed = local_writes(actors[a[0] // 2][a[1] & 15]) if op in (2, 3, 4) and a[0] // 2 < len(actors) else \
                    {key for key in read_variables if key[0] == 'Local'}
                # A callback can change a previously guarded cell that has no
                # substitution yet. Its later wait is not a pre-activation gate.
                values.update((key, None) for key in changed)
            condition = None
            if op == 0x18:
                condition = ('Global', 0, 3, a[0])
            elif op in (0x12, 0x16, 0x6e) and (a[2] & 0x7f) <= 7:
                source = 'Local' if op == 0x12 else 'Global' if op == 0x16 else 'Extra'
                index = a[0] + (256 if op == 0x16 and a[2] & 128 else 0)
                condition = (source, index, a[2] & 0x7f, a[1])
            elif op == 0xc9:
                condition = ('Item', int.from_bytes(a[:2], 'little'), 2, 0)
            elif op == 0xcc:
                condition = ('Gold', 0, 4, int.from_bytes(a[:2], 'little'))
            elif op in (0xcf, 0xd2):
                condition = ('Recruited' if op == 0xcf else 'ActiveParty', a[0], 0, 1)
            if op in CONDITIONAL:
                branches = []
                for expected, destination in ((True, next_pc), (False, pc + len(a) + a[-1])):
                    new_guards = guards
                    if condition:
                        source, index, operation, value = condition
                        variable = (source, index)
                        if source == 'Local' and values.get(variable) in ('X', 'Y'):
                            source = values[variable]
                            variable = (source, index)
                        if variable in values or source == 'Item' and values.get(('ItemsChanged', 0)) or \
                                source in ('Recruited', 'ActiveParty') and values.get(('PartyChanged', 0)):
                            stored = values.get(variable)
                            if isinstance(stored, int):
                                if compare(stored, value, operation) != expected:
                                    continue
                            # None is a value generated during this interaction;
                            # it cannot be tested against a previous frame's local.
                        else:
                            candidate = guard(source, index, operation, value, expected)
                            opposite = dict(candidate, Expected=not expected)
                            if opposite in guards:
                                continue
                            if candidate not in guards:
                                new_guards = guards + (candidate,)
                                related = [g for g in new_guards if g['Source'] == source and g['Index'] == index]
                                domain = range(2) if source in ('ActiveParty', 'Recruited') else \
                                    range(100) if source == 'Item' else range(256) if source in ('Global', 'Local', 'X', 'Y') else None
                                if domain is not None and not any(all(compare(v, g['Value'], g['Operation']) == g['Expected']
                                                                      for g in related) for v in domain):
                                    continue
                    branches.append((destination, returns, new_guards, values.copy()))
                for branch in branches:
                    enqueue(branch)
                break
            kind = None
            if op in (0xc7, 0xca, 0xcd):
                kind = 'Item'
            elif op in (0xe4, 0xe5):
                kind = 'Terrain'
            elif op == 0xc8:
                kind = 'Menu'
            elif op in (0xbb, 0xc0, 0xc1, 0xc2, 0xc3, 0xc4):
                kind = 'Talk'
            elif 0xdc <= op <= 0xe1:
                kind = 'Warp'
            elif op == 0x5a or op == 0x56 and a[1:] == bytes([0, 0]):
                kind = 'Progress'
            elif op in (0x3a, 0x45, 0x46, 0x65, 0x66):
                kind = 'Switch'
            elif op == 0xd8:
                kind = 'Encounter'
            if kind:
                record = dict(Kind=kind, Guards=list(guards))
                if kind == 'Warp':
                    record.update(Destination=int.from_bytes(a[:2], 'little') & 1023,
                                  ArrivalX=a[3], ArrivalY=a[4])
                if kind == 'Progress':
                    record['Value'] = a[0]
                if kind == 'Terrain':
                    record['Copy'] = dict(zip(('Left', 'Top', 'Right', 'Bottom', 'X', 'Y', 'Flags'), a))
                if kind == 'Encounter':
                    record['Value'] = int.from_bytes(a, 'little')
                if kind == 'Switch':
                    record.update(Source='Extra' if op in (0x3a, 0x45, 0x46) else 'Global',
                                  Index=a[1] + (256 if op in (0x65, 0x66) and a[0] & 128 else 0),
                                  Value=a[0] if op == 0x3a else 1 << (a[0] & 0x7f), Set=op in (0x3a, 0x45, 0x65))
                output.append(record)
                # A field encounter transfers control to battle. Its outcome and
                # following cinematic are not current movement destinations.
                # Stop at that boundary instead of expanding every party/ending
                # branch after it and losing the trigger to the state budget.
                if kind in ('Warp', 'Encounter'):
                    break
            # A contact handler may notify a separately scheduled room controller
            # instead of calling D8 itself. Link only a proven immediate local
            # write to a controller predicate that the written value satisfies.
            # All other native predicates still gate the resulting destination.
            write = (a[-1], int.from_bytes(a[:-1], 'little') & 255) if op in (0x4f, 0x50) else \
                (a[0], 0 if op == 0x77 else 1) if op in (0x75, 0x76, 0x77) else None
            if write is not None:
                index, written = write
                for encounter in signal_encounters:
                    signal_guards = [g for g in encounter['Guards'] if g['Source'] == 'Local' and g['Index'] == index]
                    # Require an exact event signal, not a negated bypass/history
                    # condition inherited from an earlier branch of a room loop.
                    domain = {v for v in range(256) if all(compare(v, g['Value'], g['Operation']) == g['Expected'] for g in signal_guards)}
                    if not signal_guards or domain != {written}:
                        continue
                    remaining = list(guards)
                    valid = True
                    for g in encounter['Guards']:
                        if g in signal_guards:
                            continue
                        variable = (g['Source'], g['Index'])
                        if variable in values:
                            current = values[variable]
                            # Do not guess a callback's unknown intermediate value.
                            if not isinstance(current, int) or compare(current, g['Value'], g['Operation']) != g['Expected']:
                                valid = False
                                break
                        elif g not in remaining:
                            remaining.append(g)
                    if valid:
                        remaining.append(guard('Local', index, 1, written))
                        # Reject contradictory producer/consumer predicates.
                        for source, cell in {(g['Source'], g['Index']) for g in remaining}:
                            if source not in ('Global', 'Local', 'X', 'Y'):
                                continue
                            related = [g for g in remaining if (g['Source'], g['Index']) == (source, cell)]
                            if not any(all(compare(v, g['Value'], g['Operation']) == g['Expected'] for g in related) for v in range(256)):
                                valid = False
                                break
                    if valid:
                        output.append(dict(Kind='Encounter', Value=encounter['Value'],
                            Controller=encounter['Controller'], Guards=remaining))
            if op in (0x4f, 0x50):
                values[('Local', a[-1])] = int.from_bytes(a[:-1], 'little') & 255
            elif op in (0x75, 0x76, 0x77):
                values[('Local', a[0])] = 0 if op == 0x77 else 1
            elif op in (0x48, 0x49, 0x51, 0x52, 0x53, 0x54, 0x3e, 0x74, 0x70):
                values[('Local', a[-1])] = None
            elif op in (0x20, 0x55, 0x71, 0x72, 0x73):
                values[('Local', a[0])] = None
            elif op in (0x5b, 0x5d, 0x5e, 0x5f, 0x60, 0x61, 0x63, 0x64, 0x67, 0x69, 0x6b):
                values[('Local', a[-1])] = None
            elif op == 0x56:
                values[('Global', int.from_bytes(a[1:], 'little'))] = a[0]
            elif op == 0x5a:
                values[('Global', 0)] = a[0]
            elif op == 0x3a:
                values[('Extra', a[1])] = a[0]
            elif op in (0x45, 0x46):
                values[('Extra', a[1])] = None
            elif op in (0x65, 0x66):
                # gbiton/off encode the upper bank in bit7 of the bit selector.
                values[('Global', a[1] + (256 if a[0] & 128 else 0))] = None
            elif op in (0x21, 0x22):
                # Coordinate provenance is local to this path. Another actor's
                # position or an overwritten scratch cell is not the player.
                values[('Local', a[1])] = 'X' if op == 0x22 and a[0] == 0 else None
                values[('Local', a[2])] = 'Y' if op == 0x22 and a[0] == 0 else None
            elif op in (0xc7, 0xca, 0xcb):
                values[('ItemsChanged', 0)] = True
            elif op in (0xcd, 0xce):
                values[('Gold', 0)] = None
            elif op in (0xd0, 0xd1, 0xd3, 0xd4, 0xd6):
                values[('PartyChanged', 0)] = True
            pc = next_pc
    return simplify(output), True


def startup_actions(code, actors, incomplete=None):
    coords = coordinate_locals(code, actors)
    output = []
    for actor, entries in enumerate(actors):
        init = walk(code, [entries[0]])
        returns = [pc for pc, (op, _) in init.items() if op == 0]
        if not returns:
            continue
        actions, complete = paths(code, actors, min(returns) + 1, coords)
        if not complete:
            if incomplete is not None:
                incomplete.append(dict(Actor=actor, Function='Startup', Start=min(returns) + 1))
            continue
        output.extend(dict(action, Actor=actor) for action in actions)
    return output


def region_actions(code, actors, incomplete=None, startup=None):
    output = []
    if startup is None:
        startup = startup_actions(code, actors, incomplete)
    for action in startup:
        actor = action['Actor']
        if action['Kind'] not in ('Warp', 'Progress', 'Switch', 'Encounter', 'Terrain'):
            continue
        x, y = set(range(256)), set(range(256))
        for g in action['Guards']:
            if g['Source'] in ('X', 'Y'):
                axis = x if g['Source'] == 'X' else y
                axis.intersection_update(v for v in range(256)
                                         if compare(v, g['Value'], g['Operation']) == g['Expected'])
        if not x or not y or len(x) == len(y) == 256:
            continue
        guards = [g for g in action['Guards'] if g['Source'] not in ('X', 'Y')]
        for left, right in intervals(x):
            for top, bottom in intervals(y):
                record = dict(Actor=actor, Kind=action['Kind'], Destination=action.get('Destination', -1),
                              Value=action.get('Value', 0), Left=left, Top=top,
                              Right=right, Bottom=bottom, Guards=guards)
                if action['Kind'] == 'Switch':
                    record.update(Source=action['Source'], Index=action['Index'], Set=action['Set'])
                if record not in output:
                    output.append(record)
    return simplify(output)
