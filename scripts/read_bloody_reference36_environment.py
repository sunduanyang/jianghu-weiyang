"""Read only Root-authorized first-bout environment enum/seed fields.

Uses one specified TypeInfo GOT chain and five specified four-byte game
fields. No accounts, wallets, strings, whole-memory dumps, or writes.
"""
from datetime import datetime,timezone
import json
from pathlib import Path
import re
import struct
import subprocess

ROOT=Path(__file__).resolve().parents[1]
ADB='D:/codex-bloody-workspace-20261028/output/bloody-full-port-20261028/tools/android-reference/sdk/platform-tools/adb.exe'
PREFIX=[ADB,'-P','5051','-s','emulator-5586']
BASE=0x75ca06301000
FIELDS=[('GameMode',0x94),('CampaignRegion',0x98),('CurrentSeed',0x104),
    ('BBLevelType',0x108),('ShopRegion',0x110)]


def run(*args):return subprocess.run(PREFIX+list(args),capture_output=True,timeout=20)


def main():
    assert run('shell','getprop','ro.boot.qemu.avd_name').stdout.strip()==b'bloody-reference36'
    assert run('shell','pidof','com.tibith.badboxing').stdout.strip()==b'2334'
    result=run('shell','cat','/proc/2334/maps')
    if result.returncode:raise RuntimeError('Maps permission refused')
    lines=result.stdout.decode().splitlines()
    proof=next(x for x in lines if x.startswith(f'{BASE:x}-')and'libil2cpp.so'in x and x.split()[2]=='00000000')
    ranges=[]
    for line in lines:
        m=re.match(r'([0-9a-f]+)-([0-9a-f]+)\s+(\S+)',line)
        if m and'r'in m[3]:ranges.append((int(m[1],16),int(m[2],16)))
    reads=[]
    def read(address,size,label):
        if address%size or not any(low<=address and address+size<=high for low,high in ranges):
            raise RuntimeError('Specified field is not aligned/readable: '+label)
        r=run('exec-out','dd','if=/proc/2334/mem',f'bs={size}',f'skip={address//size}','count=1','status=none')
        if r.returncode or len(r.stdout)!=size:raise RuntimeError('Memory permission/read refused: '+label)
        value=struct.unpack('<Q'if size==8 else'<i',r.stdout)[0]
        reads.append(dict(label=label,address=hex(address),bytes=size,value=hex(value)if size==8 else value))
        return value
    variable=read(BASE+0x7212238,8,'hq TypeInfo GOT')
    klass=read(variable,8,'hq Il2CppClass pointer variable')
    static=read(klass+0xb8,8,'hq Il2CppClass.static_fields')
    values={name:read(static+offset,4,name)for name,offset in FIELDS}
    report=dict(captured_utc=datetime.now(timezone.utc).isoformat(),authorization='Root explicitly approved only TypeInfo GOT VA0x7212238 chain, Class+0xb8 and five listed gameplay int fields.',pid=2334,elf_base=hex(BASE),base_mapping=proof,values=values,reads=reads,total_memory_bytes_read=sum(r['bytes']for r in reads),apk_modified=False,game_fields_modified=False,instructions_modified=False,whole_memory_dump=False,account_wallet_or_string_fields_read=False,selinux_changed=False)
    output=ROOT/'output/bloody-full-port-20261028/apk-reference36-live/first-bout-environment.json'
    output.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))


if __name__=='__main__':main()
