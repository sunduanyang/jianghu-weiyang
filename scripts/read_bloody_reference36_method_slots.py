"""Read the four explicitly requested pointers from this task's own APK.

Reads /proc maps, two specified metadata-slot qwords, then each MethodInfo's
first qword if readable. Never dumps memory, patches instructions, changes
SELinux, calls a game method, or writes any game field.
"""
from datetime import datetime,timezone
import io
import json
from pathlib import Path
import re
import struct
import subprocess
import zipfile
from elftools.elf.elffile import ELFFile

ROOT=Path(__file__).resolve().parents[1]
ADB=Path('D:/codex-bloody-workspace-20261028/output/bloody-full-port-20261028/tools/android-reference/sdk/platform-tools/adb.exe')
PREFIX=[str(ADB),'-P','5051','-s','emulator-5586']
APK=Path('D:/BaiduNetdiskDownload/Bloody-Bastards-v5.0.3-mod-apkvision.apk')
SLOTS=[0x7212de8,0x7212df0]


def command(*args):
    return subprocess.run(PREFIX+list(args),capture_output=True,timeout=20)


def main():
    identity=command('shell','getprop','ro.boot.qemu.avd_name')
    if identity.returncode or identity.stdout.strip()!=b'bloody-reference36':
        raise RuntimeError('Not the task dedicated AVD')
    result=command('shell','pidof','com.tibith.badboxing')
    pids=result.stdout.decode().split()
    if result.returncode or len(pids)!=1:raise RuntimeError('Expected one original APK PID')
    pid=int(pids[0]);result=command('shell','cat',f'/proc/{pid}/maps')
    if result.returncode:raise RuntimeError('Maps access refused: '+result.stderr.decode(errors='replace'))
    maps=[]
    for line in result.stdout.decode().splitlines():
        match=re.match(r'([0-9a-f]+)-([0-9a-f]+)\s+(\S+)\s+([0-9a-f]+)\s+\S+\s+\d+\s*(.*)',line)
        if match:maps.append(dict(start=int(match[1],16),end=int(match[2],16),permissions=match[3],offset=int(match[4],16),path=match[5],source_line=line))
    with zipfile.ZipFile(APK)as archive:
        info=archive.getinfo('lib/arm64-v8a/libil2cpp.so');blob=archive.read(info)
    with APK.open('rb')as stream:
        stream.seek(info.header_offset);header=stream.read(30)
        name_length,extra_length=struct.unpack_from('<HH',header,26)
        blob_offset=info.header_offset+30+name_length+extra_length
    elf=ELFFile(io.BytesIO(blob));loads=[dict(offset=s['p_offset'],va=s['p_vaddr'],file_size=s['p_filesz'],memory_size=s['p_memsz'],flags=s['p_flags'])for s in elf.iter_segments()if s['p_type']=='PT_LOAD']
    direct=[m for m in maps if 'libil2cpp.so' in m['path']and not m['path'].startswith('[anon:')]
    candidates=[]
    for mapping in direct or [m for m in maps if m['path'].endswith('/base.apk')]:
        relative=mapping['offset']-(0 if direct else blob_offset)
        for segment in loads:
            low=segment['offset']//4096*4096
            if low<=relative<(segment['offset']+segment['file_size']+4095)//4096*4096:
                mapped_va=segment['va']//4096*4096+(relative-low)
                candidates.append(dict(base=mapping['start']-mapped_va,mapping=mapping['source_line'],relative_file_offset=hex(relative),elf_segment=segment))
                break
    groups={}
    for candidate in candidates:groups.setdefault(candidate['base'],[]).append(candidate)
    if not groups:raise RuntimeError('Could not prove source ELF PT_LOAD base from actual maps')
    ordered=sorted(groups.items(),key=lambda item:len(item[1]),reverse=True)
    if len(ordered)>1 and len(ordered[0][1])==len(ordered[1][1]):raise RuntimeError('Ambiguous ELF base')
    base,evidence=ordered[0]
    def readable(address):return any('r' in m['permissions']and m['start']<=address and address+8<=m['end']for m in maps)
    reads=[]
    def qword(address,label):
        if address%8 or not readable(address):return dict(label=label,address=hex(address),read=False,reason='Not an aligned readable mapped qword')
        value=command('exec-out','dd',f'if=/proc/{pid}/mem','bs=8',f'skip={address//8}','count=1','status=none')
        row=dict(label=label,address=hex(address),read=value.returncode==0 and len(value.stdout)==8,exit_code=value.returncode,stdout_bytes=len(value.stdout),stderr=value.stderr.decode(errors='replace'))
        if row['read']:row['value']=hex(struct.unpack('<Q',value.stdout)[0])
        reads.append(row);return row
    slots=[]
    for va in SLOTS:
        slot=qword(base+va,'metadata slot '+hex(va));row=dict(slot_va=hex(va),slot=slot)
        if slot.get('read'):
            pointer=int(slot['value'],16);method=qword(pointer,'MethodInfo first qword for '+hex(va));row['method_info_first_qword']=method
            if method.get('read'):
                code=int(method['value'],16);row['code_va']=hex(code-base)
                row['code_inside_source_executable_segment']=any(s['flags']&1 and s['va']<=code-base<s['va']+s['memory_size']for s in loads)
        slots.append(row)
    report=dict(captured_utc=datetime.now(timezone.utc).isoformat(),device='emulator-5586',adb_server_port=5051,pid=pid,source_apk=str(APK),library='libil2cpp.so',elf_base=hex(base),apk_elf_data_offset=hex(blob_offset),direct_library_mapping=bool(direct),base_evidence=evidence,slots=slots,qwords_read=len(reads),maximum_qwords_requested=4,apk_modified=False,game_fields_modified=False,instructions_modified=False,selinux_changed=False)
    output=ROOT/'output/bloody-full-port-20261028/apk-reference36-live/tutorial-method-slots.json'
    output.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))


if __name__=='__main__':main()
