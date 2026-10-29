"""Small offline source inspection. Never accesses the retained Android guest."""
import json, struct, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'output/bloody-full-port-20261028/ai-check/vendor'))
from capstone import Cs,CS_ARCH_ARM64,CS_MODE_ARM
from elftools.elf.elffile import ELFFile
lib=ROOT/'output/bloody-reference-20261028/libil2cpp.so'
def read(va,size):
    with lib.open('rb') as f:
        elf=ELFFile(f)
        for s in elf.iter_segments():
            if s['p_type']=='PT_LOAD' and s['p_vaddr']<=va< s['p_vaddr']+s['p_filesz']:
                f.seek(s['p_offset']+va-s['p_vaddr']);return f.read(size)
    raise ValueError(hex(va))
def dis(va,size):
    for i in Cs(CS_ARCH_ARM64,CS_MODE_ARM).disasm(read(va,size),va):print(f'{i.address:x}: {i.mnemonic:8} {i.op_str}')
if __name__=='__main__':
    if len(sys.argv)>1:dis(int(sys.argv[1],16),int(sys.argv[2],16))
    else:
        d=json.loads((ROOT/'output/bloody-code-audit-20261028/game-metadata.json').read_text(encoding='utf-8'))
        for t in d['types']:
            if t['type'] in ('RegionConfiguration','StageGraphics','StageBigBuildings','StageDecorations'):
                print(t['type']);print(json.dumps(t.get('fields'),ensure_ascii=False))
        for va in (0x120b1cc,0x120a624):print(hex(va),struct.unpack('<f',read(va,4))[0])
        dis(0x36c9b00,0x178)
