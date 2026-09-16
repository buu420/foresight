"""Independent audit of PC operand sizes and branch reachability.

CTViewer supplies the basic lengths. Explicit PC helper reads below are checked
against its source; native opcode 55 is a READ (RVA168110), unlike its comment.
"""
import json
from pathlib import Path
import struct
from assets import read_asset
from connections import scene_info

ROOT = Path(__file__).parent
LENGTH = {int(k): v for k, v in json.loads((ROOT/'op-lengths.json').read_text())['fixed'].items()}
LENGTH.update({0x48:3,0x49:3,0x4a:3,0x4b:4,0x4c:3,0x4d:3,
               0xbb:2,0xc0:3,0xc1:2,0xc2:2,0xc3:3,0xc4:3,
               0xc7:1,0xca:2,0xcb:2,0xd5:3,0xd7:3,0xa2:6,0xa3:2,0xfc:1,
               # PC handlers verified in Ghidra, 2026-09-15. C7 reads a local
               # word (item and category), not a second immediate category byte.
               0x79:6,0x85:3,0x86:3,0x93:1,0x9b:2,0xbe:7,0xbf:1,
               **{x:5 for x in range(0xdc,0xe2)}})
CONDITIONAL = {0x12,0x13,0x14,0x15,0x16,0x18,0x1a,0x27,0x28,0x2d,0x30,0x31,0x79,
               *range(0x34,0x3a),0x3b,0x3c,*range(0x3f,0x45),0x6e,0xc9,0xcc,0xcf,0xd2}

def packet(scene):
    info=scene_info(scene)
    raw=read_asset(f"Game/field/atel/Atel_{info['script']:04}.dat")
    n=raw[0]; base=1+n*32
    entries=[[v-n*32 for v in struct.unpack_from('<16H',raw,1+i*32)] for i in range(n)]
    return raw[base:],entries,base

def instruction(code,pc):
    op=code[pc]; n=LENGTH.get(op)
    if op==0x4e: n=2+struct.unpack_from('<H',code,pc+3)[0]
    if op==0x2e:
        mode=code[pc+1]; n=5 if mode&0x40 else 3 if mode&0x80 else None
    if op==0x88:
        mode=code[pc+1]
        n=1 if mode==0 else 3 if mode in (0x20,0x30) else 4 if 0x40<=mode<0x60 else 2 if 0x80<=mode<0x90 else None
    if op==0xf1: n=1 if code[pc+1]==0 else 2
    if op==0xff: n=4 if code[pc+1] in (0x90,0x97) else 1
    if n is None or pc+1+n>len(code): return op,None
    return op,code[pc+1:pc+1+n]

def walk(code,starts):
    pending=list(starts); found={}
    while pending:
        pc=pending.pop()
        while 0<=pc<len(code) and pc not in found:
            op,args=instruction(code,pc); found[pc]=(op,args)
            if args is None or op in (0x00,0xb2): break
            if op in (0x10,0x11):
                pc+=1+(args[0] if op==0x10 else -args[0]); continue
            if op in CONDITIONAL: pending.append(pc+len(args)+args[-1])
            pc+=1+len(args)
    return found

def actor_ops(code,entries):
    init=walk(code,[entries[0]])
    returns=[pc for pc,(op,a) in init.items() if op==0]
    extra=[min(returns)+1] if returns else []
    return init|walk(code,[*entries[1:],*extra])

if __name__=='__main__':
    scenes=[208,210,212,213,214,215,216,217,218,219,221,223,224,225,226,227,228,229,230,231,232,233,234,235,259,437,344,84,464,465]
    out=[]
    for scene in scenes:
        code,entries,base=packet(scene); facts=[]; errors=[]
        for actor,entry in enumerate(entries):
            ops=actor_ops(code,entry)
            for pc,(op,a) in sorted(ops.items()):
                if a is None: errors.append([actor,hex(base+pc),hex(op)])
                if op in (0x5a,0x16,0x18,0x56,0x65,0x66,0x67,0x81,0x82,0x83,0x8b,0x8d,0xbb,0xc0,0xc1,0xc2,0xc3,0xc4,0xdc,0xdd,0xde,0xdf,0xe0,0xe1):
                    facts.append(dict(actor=actor,offset=hex(base+pc),opcode=hex(op),args=a.hex() if a is not None else None))
        points=sorted({code[pc+1] for e in entries for pc,(op,a) in actor_ops(code,e).items() if op==0x5a and a})
        print(scene,scene_info(scene)['name'],'sets',points,'unknown',errors[:8])
        out.append(dict(scene=scene,base=base,points=points,errors=errors,facts=facts))
    (ROOT/'verified-script-facts-root.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
