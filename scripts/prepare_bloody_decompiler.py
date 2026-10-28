"""Prepare native symbols/prototypes for reproducible combat decompilation.

Cpp2IL's AddressAttribute RVA/Offset values in this APK are file offsets.
Map through PT_LOAD, rather than assuming ELF virtual addresses equal offsets.
"""
import argparse
import json
from pathlib import Path

from elftools.elf.elffile import ELFFile
from audit_bloody_combat import GROUPS


def prepare(binary, metadata, destination):
    with binary.open('rb') as stream:
        segments=[dict(s.header) for s in ELFFile(stream).iter_segments() if s['p_type']=='PT_LOAD']
    def address(text):
        offset=int(text,16)
        return next(offset-s['p_offset']+s['p_vaddr'] for s in segments
                    if s['p_offset']<=offset<s['p_offset']+s['p_filesz'])
    roots={name for group in GROUPS.values() for name in group.split()}
    methods=[];types={};seen=set()
    for file in metadata:
        catalog=json.loads(file.read_text(encoding='utf-8-sig'))
        for row in catalog['types']:
            types.setdefault(row['type'],dict(name=row['type'],base=row['base'],fields=row['fields']))
            for method in row['methods']:
                attr=method['attributes']
                if not attr.get('RVA') or not attr.get('Length'):continue
                va=address(attr.get('Offset',attr['RVA']));length=int(attr['Length'],16)
                if va in seen or not length:continue
                seen.add(va)
                signature=method['signature'];returns=signature.split(' ',1)[0]
                arguments=signature.split('(',1)[1].rsplit(')',1)[0]
                params=[];depth=0;last=0
                for index,char in enumerate(arguments):
                    if char in '<([':depth+=1
                    elif char in '>)]':depth-=1
                    elif char==',' and not depth:params.append(arguments[last:index]);last=index+1
                if arguments:params.append(arguments[last:])
                methods.append(dict(address=hex(va),length=length,
                    name=row['type']+'__'+method['name']+'_'+hex(va)[2:],type=row['type'],
                    returns=returns,params=params,instance=not method['static'],signature=signature,
                    decompile=row['type'].split('/')[0] in roots))
    destination.write_text(json.dumps(dict(types=list(types.values()),methods=methods)),encoding='utf-8')
    print(json.dumps(dict(functions=len(methods),selected=sum(m['decompile'] for m in methods),types=len(types))))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--binary',type=Path,required=True)
    parser.add_argument('--metadata',type=Path,nargs='+',required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();prepare(args.binary,args.metadata,args.output)
