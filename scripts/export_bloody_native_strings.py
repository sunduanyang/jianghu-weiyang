"""Recover the original IL2CPP string table used by the obfuscated getters.

The source a::.cctor (VA 0x3630bf4) transforms its FieldRVA data by XORing
the byte index and 0xaa. Getter a_ passes string index, byte offset and byte
length to Encoding.UTF8.GetString. Do not use Cpp2IL's dummy DLL FieldRVA for
this nested field: its emitted RVA points at generated CIL method bodies.
"""
import json
import re
from pathlib import Path
import capstone


def main():
    root=Path(__file__).resolve().parents[1]
    metadata=(root/'output/bloody-reference-20261028/global-metadata.dat').read_bytes()
    binary=(root/'output/bloody-reference-20261028/libil2cpp.so').read_bytes()
    catalog=json.loads((root/'output/bloody-code-audit-20261028/game-metadata.json').read_text())
    cls=next(t for t in catalog['types'] if t['type']=='<PrivateImplementationDetails>{F695C944-5042-4FC7-931B-E302041A1286}.a')
    anchor=b'Universal2D';offset=332
    cipher=bytes(value^((offset+i)&255)^0xaa for i,value in enumerate(anchor))
    found=metadata.find(cipher)
    assert found>=0 and metadata.find(cipher,found+1)<0,'String-pool anchor must be unique'
    start=found-offset
    pool=bytes(value^(i&255)^0xaa for i,value in enumerate(metadata[start:start+129991]))
    disassembler=capstone.Cs(capstone.CS_ARCH_ARM64,capstone.CS_MODE_LITTLE_ENDIAN)
    records=[]
    for method in cls['methods']:
        attr=method['attributes'];file_offset=int(attr['Offset'],16);size=int(attr['Length'],16);va=file_offset+0x4000
        registers={};calls_decoder=False
        for instruction in disassembler.disasm(binary[file_offset:file_offset+size],va):
            if instruction.mnemonic in ['mov','movk']:
                match=re.fullmatch(r'(w[012]), (?:#(-?0x[0-9a-f]+|-?\d+)|wzr)(?:, lsl #(\d+))?',instruction.op_str)
                if match:
                    register=match[1];value=int(match[2],0) if match[2] else 0;shift=int(match[3] or 0)
                    registers[register]=((registers.get(register,0)&~(0xffff<<shift))|(value<<shift)) if instruction.mnemonic=='movk' else value
            if instruction.mnemonic in ['b','bl'] and instruction.op_str=='#0x358eaf0':calls_decoder=True
        if not calls_decoder or not all(r in registers for r in ['w0','w1','w2']):continue
        index,offset,length=(registers[r] for r in ['w0','w1','w2'])
        assert 0<=offset<=offset+length<=len(pool),(method['name'],registers)
        text=pool[offset:offset+length].decode('utf-8')
        records.append(dict(getter=method['name'],va=hex(va),index=index,offset=offset,length=length,text=text))
    out=root/'output/bloody-full-port-20261028/native-strings.json'
    out.write_text(json.dumps(dict(source='Original global-metadata.dat FieldRVA bytes',pool_offset=start,pool_length=len(pool),decoder_va='0x358eaf0',records=records),ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(output=str(out),strings=len(records),pool_offset=start)))


if __name__=='__main__':main()
