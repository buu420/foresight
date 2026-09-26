"""Export PC world bytecode using CTViewer's explicit operand reads.

The reference is retained by source hash. This produces a disassembly, not
reconstructed original script source; unknown bytes are never guessed through.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re


def table_from_source(path):
    source = path.read_text(encoding='utf8')
    cases = list(re.finditer(r'^        (0x[0-9A-Fa-f]+) =>', source, re.M))
    table = {}
    for i, match in enumerate(cases):
        block = source[match.end():cases[i + 1].start() if i + 1 < len(cases) else len(source)]
        size = sum(int(bits) // 8 for bits in re.findall(r'data\.read_[ui](8|16|32)\b', block))
        size += 3 * block.count('read_24_bit_address(data)')
        size += 5 * block.count('Destination::from_cursor(data, mode)')
        preceding = source[max(0, match.start() - 100):match.start()]
        names = re.findall(r'// "([^"\n]+)', preceding)
        name = names[-1] if names else f'op_{int(match[1], 16):02X}'
        table[int(match[1], 16)] = (size, name)
    if len(table) != len(cases) or table.get(5, (None,))[0] != 5:
        raise ValueError('Unexpected CTViewer decoder structure')
    # Installed PC dispatch 268510 sends opcode53 to 26F030. That handler
    # reads PC+1 and PC+2, and advances three bytes (ECX+0C = 3). The
    # community decoder consumes a third operand and loses synchronization
    # in Event_0006 at 09D1. Native bytes and all eight full streams agree.
    table[0x53] = (2, table[0x53][1])
    return table


def export(raw_root, decoder, out):
    table = table_from_source(decoder)
    out.mkdir(parents=True, exist_ok=True)
    report = dict(reference=str(decoder.resolve()), referenceSha256=hashlib.sha256(decoder.read_bytes()).hexdigest(),
                  opcodeCount=len(table), scripts=[])
    (out / 'reference_decoder.rs').write_bytes(decoder.read_bytes())
    report['retainedReference'] = 'reference_decoder.rs'
    report['upstream'] = 'https://github.com/GitExl/CTViewer'
    (out / 'opcodes.json').write_text(json.dumps({f'{op:02X}': dict(operandBytes=n, name=name) for op, (n, name) in sorted(table.items())}, indent=2) + '\n', encoding='utf8')
    for path in sorted((raw_root / 'Game/world/esl').glob('Event_*.dat')):
        raw = path.read_bytes()
        pc = 0
        rows = ['PC world event bytecode; offsets are file offsets.', 'Raw operands retained. Native code remains authoritative.', '']
        count = 0
        failures = []
        while pc < len(raw):
            op = raw[pc]
            if op not in table or pc + 1 + table[op][0] > len(raw):
                failures.append(dict(offset=pc, opcode=op, remaining=len(raw) - pc))
                rows.append(f'{pc:04X}: UNDECODED TAIL ({len(raw)-pc} bytes); see the complete raw file.')
                break
            n, name = table[op]
            args = raw[pc+1:pc+1+n]
            detail = ''
            if op == 5:
                detail = f' scene={int.from_bytes(args[:2], "little")}, facing={args[2]}, x={args[3]}, y={args[4]}'
            elif op in (8, 9, 0x36, 0x43):
                detail = f' target=0x{int.from_bytes(args[:2], "little") - 0x400:04X}'
            elif op == 0x1a:
                detail = f' target=0x{int.from_bytes(args[:2], "little") - 0x400 + 1:04X}'
            elif 0x1b <= op <= 0x27 or op in (0x4c, 0x4d):
                detail = f' branch=0x{pc + int.from_bytes(args[-1:], "little", signed=True):04X}'
            rows.append(f'{pc:04X}  {op:02X} {args.hex(" "):26} {name}{detail}')
            pc += n + 1
            count += 1
        (out / (path.stem + '.txt')).write_text('\n'.join(rows) + '\n', encoding='utf8')
        report['scripts'].append(dict(path=path.name, bytes=len(raw), decodedBytes=pc, instructions=count,
                                      status='decoded' if not failures else 'partial', failures=failures))
    (out / 'manifest.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--raw-root', type=Path, required=True)
    p.add_argument('--decoder-source', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    export(a.raw_root, a.decoder_source, a.output)
