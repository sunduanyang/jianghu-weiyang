"""Read-only selected round/respawn/timer disassembly for recovery review."""
import json
from pathlib import Path
import capstone
from elftools.elf.elffile import ELFFile

ROOT=Path(__file__).resolve().parents[1]
BINARY=ROOT/'output/bloody-reference-20261028/libil2cpp.so'
METADATA=[ROOT/'output/bloody-code-audit-20261028/game-metadata.json',*list((ROOT/'output/bloody-full-port-20261028/metadata').glob('*.json'))]
with BINARY.open('rb') as stream:
    segments=[dict(s.header) for s in ELFFile(stream).iter_segments() if s['p_type']=='PT_LOAD']
raw=BINARY.read_bytes()
def va(offset):
    return next(offset-s['p_offset']+s['p_vaddr'] for s in segments if s['p_offset']<=offset<s['p_offset']+s['p_filesz'])
symbols={};methods=[]
for file in METADATA:
    data=json.loads(file.read_text(encoding='utf-8-sig'))
    for typ in data['types']:
        for method in typ['methods']:
            attr=method.get('attributes',{});offset=attr.get('Offset',attr.get('RVA'))
            if not offset:continue
            address=va(int(offset,16));symbols.setdefault(address,[]).append(method['signature'])
            methods.append((typ['type'],method['name'],address,int(offset,16),int(attr.get('Length','0'),16)))
selected={'MainNetworkRunner':{'RespawnAllFighters','DespawnAllFighters','cvy','CacheConfig','TryGetCachedConfig'},
          'MainNetworkRunner/dr':{'bre','cvm','dce'},
          'Fusion.TickTimer':{'CreateFromSeconds','RemainingTime','Expired'},
          'CharacterConfigApplier':{'ApplyAll','cfb'},
          'NetworkedGameController':{'ctf','HandlePlayerDeath','ctc','crw'}}
decoder=capstone.Cs(capstone.CS_ARCH_ARM64,capstone.CS_MODE_LITTLE_ENDIAN)
lines=[]
for typ,name,address,offset,length in methods:
    if typ not in selected or name not in selected[typ]:continue
    lines.append(f'\n### {typ}.{name} VA {address:#x} FILE {offset:#x} LENGTH {length:#x}')
    for op in decoder.disasm(raw[offset:offset+length],address):
        note=''
        if op.mnemonic in ('bl','b') and op.op_str.startswith('#0x'):
            target=int(op.op_str[1:],16)
            if target in symbols:note=' ; '+' | '.join(symbols[target][:3])
        lines.append(f'{op.address:x}: {op.mnemonic} {op.op_str}{note}')
destination=ROOT/'output/bloody-full-port-20261028/combat-check/root-match-current-review.asm'
destination.write_text('\n'.join(lines),encoding='utf-8')
print(destination)
