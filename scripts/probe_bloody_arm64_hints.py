"""Produce tiny standalone ARM64 compatibility probes; never patch the game.

These files require a real ARM64 Android device or the target image's own
native-bridge program runner/binfmt integration. They do not run themselves.
An x86 shell's generic exec error is not evidence of an unsupported SEVL.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct


def elf(words):
    code=struct.pack('<'+'I'*len(words),*words)
    # Bionic/native-bridge validates ELF section headers and dynamic tables
    # even for an executable with no imports. Produce a complete tiny PIE,
    # rather than the section-less ET_EXEC accepted by Linux's kernel alone.
    names=b'\0.text\0.dynstr\0.dynsym\0.hash\0.dynamic\0.shstrtab\0'
    dynamic=b''.join(struct.pack('<qQ',tag,value)for tag,value in [
        (4,0x140),(5,0x120),(6,0x128),(10,1),(11,24),
        (0x6ffffffb,0x08000000),(0,0)])
    size=0x240+7*64
    header=struct.pack('<16sHHIQQQIHHHHHH',
        b'\x7fELF\x02\x01\x01'+b'\0'*9,3,183,1,
        0x100,64,0x240,0,64,56,2,64,7,6)
    program=struct.pack('<IIQQQQQQ',1,5,0,0,0,size,size,0x1000)
    program+=struct.pack('<IIQQQQQQ',2,4,0x160,0x160,0x160,len(dynamic),len(dynamic),8)
    result=bytearray(size);result[:64]=header;result[64:64+len(program)]=program
    result[0x100:0x100+len(code)]=code
    result[0x140:0x150]=struct.pack('<4I',1,1,0,0)
    result[0x160:0x160+len(dynamic)]=dynamic
    result[0x1d0:0x1d0+len(names)]=names
    sections=[(0,0,0,0,0,0,0,0,0,0),
        (names.index(b'.text'),1,6,0x100,0x100,len(code),0,0,4,0),
        (names.index(b'.dynstr'),3,2,0x120,0x120,1,0,0,1,0),
        (names.index(b'.dynsym'),11,2,0x128,0x128,24,2,1,8,24),
        (names.index(b'.hash'),5,2,0x140,0x140,16,3,0,4,4),
        (names.index(b'.dynamic'),6,2,0x160,0x160,len(dynamic),2,0,8,16),
        (names.index(b'.shstrtab'),3,0,0,0x1d0,len(names),0,0,1,0)]
    for index,section in enumerate(sections):
        result[0x240+index*64:0x280+index*64]=struct.pack('<IIQQQQIIQQ',*section)
    return bytes(result)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=Path(
        'output/bloody-full-port-20261028/tools/android-reference/arm64-hint-probe'))
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    # x0=0; x8=__NR_exit(93); svc 0. The WFE after SEVL must consume
    # the local event immediately, rather than wait for an external event.
    exit_words=[0xd2800000,0xd2800ba8,0xd4000001]
    programs={
        'arm64-nop-control':[0xd503201f]+exit_words,
        'arm64-sevl-wfe':[0xd50320bf,0xd503205f]+exit_words,
    }
    rows=[]
    for name,words in programs.items():
        data=elf(words);path=args.output/name;path.write_bytes(data)
        rows.append(dict(path=str(path),sha256=hashlib.sha256(data).hexdigest(),
            bytes=len(data),instruction_words=[f'0x{x:08x}' for x in words]))
    manifest=dict(scope='ARM64 translation compatibility only; not game parity',
        programs=rows,expected_exit_status=0,
        acceptance='The NOP control must execute successfully through the same '
            'native bridge first, then SEVL/WFE must also exit zero without '
            'undefined-instruction diagnostics. Generic x86 exec format or '
            'missing runner failures do not diagnose the guest instruction.',
        runner='Use only a runner/binfmt handler actually found on that image; '
            'possible name ndk_translation_program_runner is not asserted present.',
        apk_modified=False,executed=False)
    (args.output/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    print(json.dumps(manifest,indent=2))


if __name__=='__main__':main()
