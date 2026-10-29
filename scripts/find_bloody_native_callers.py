"""Find direct AArch64 B/BL references using ELF virtual addresses."""
import argparse
import bisect
import json
from pathlib import Path

import numpy as np
from elftools.elf.elffile import ELFFile

ROOT=Path(__file__).resolve().parents[1]

def main(targets):
    source=ROOT/'output/bloody-full-port-20261028/ghidra-catalog.json'
    catalog=json.loads(source.read_text(encoding='utf-8'))
    methods=sorted(catalog['methods'],key=lambda m:int(m['address'],16))
    starts=[int(m['address'],16)for m in methods]
    found=[]
    with (ROOT/'output/bloody-reference-20261028/libil2cpp.so').open('rb')as stream:
        elf=ELFFile(stream)
        for segment in elf.iter_segments():
            if segment['p_type']!='PT_LOAD'or not(segment['p_flags']&1):continue
            raw=segment.data();words=np.frombuffer(raw[:len(raw)//4*4],dtype='<u4')
            indices=np.flatnonzero((words&0x7c000000)==0x14000000)
            immediate=(words[indices]&0x03ffffff).astype(np.int64)
            immediate[immediate>=1<<25]-=1<<26
            addresses=segment['p_vaddr']+indices*4
            destinations=addresses.astype(np.int64)+immediate*4
            for target in targets:
                for position in np.flatnonzero(destinations==target):
                    address=int(addresses[position]);at=bisect.bisect_right(starts,address)-1
                    method=methods[at]if at>=0 else None
                    if method is not None and address>=starts[at]+method['length']:method=None
                    found.append(dict(target=hex(target),address=hex(address),instruction='BL'if int(words[indices[position]])&0x80000000 else'B',caller=method))
    print(json.dumps(found,ensure_ascii=False,indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('targets',nargs='+',type=lambda value:int(value,0))
    main(parser.parse_args().targets)
