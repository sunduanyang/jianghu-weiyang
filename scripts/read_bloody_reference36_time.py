"""Read only the two explicitly authorized TimeOfDayController bytes.

The original get_Instance 0x3687a50 establishes GOT VA0x7212e08, class
static_fields+0xb8, singleton slot0. get_TimeOfDay 0x3687cf8 is LDRB +0x20.
No input, heap scanning, game writes, security changes, or game calls.
"""
from datetime import datetime,timezone
import json,re,struct,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
ADB='D:/codex-bloody-workspace-20261028/output/bloody-full-port-20261028/tools/android-reference/sdk/platform-tools/adb.exe'
PREFIX=[ADB,'-P','5051','-s','emulator-5586'];PID=2334;BASE=0x75ca06301000
def run(*args):return subprocess.run(PREFIX+list(args),capture_output=True,timeout=20)
def main():
    if run('shell','getprop','ro.boot.qemu.avd_name').stdout.strip()!=b'bloody-reference36':raise RuntimeError('Dedicated AVD identity changed')
    if run('shell','pidof','com.tibith.badboxing').stdout.strip()!=b'2334':raise RuntimeError('Original reference PID changed; no reads attempted')
    maps=run('shell','cat','/proc/2334/maps')
    if maps.returncode:raise RuntimeError('Source maps refused')
    lines=maps.stdout.decode().splitlines()
    proof=next(line for line in lines if line.startswith(f'{BASE:x}-') and 'libil2cpp.so' in line and line.split()[2]=='00000000')
    ranges=[]
    for line in lines:
        m=re.match(r'([0-9a-f]+)-([0-9a-f]+)\s+(\S+)',line)
        if m and 'r' in m[3]:ranges.append((int(m[1],16),int(m[2],16)))
    reads=[]
    def read(address,size,label):
        if address%size or not any(low<=address and address+size<=high for low,high in ranges):raise RuntimeError('Authorized address is not readable: '+label)
        result=run('exec-out','dd','if=/proc/2334/mem',f'bs={size}',f'skip={address//size}','count=1','status=none')
        if result.returncode or len(result.stdout)!=size:raise RuntimeError('Authorized source read refused: '+label)
        value=struct.unpack('<Q' if size==8 else '<B',result.stdout)[0]
        reads.append(dict(label=label,address=hex(address),bytes=size,value=hex(value) if size==8 else value));return value
    variable=read(BASE+0x7212e08,8,'TimeOfDayController TypeInfo GOT')
    klass=read(variable,8,'TimeOfDayController Il2CppClass pointer variable')
    static=read(klass+0xb8,8,'TimeOfDayController Il2CppClass.static_fields')
    instance=read(static,8,'TimeOfDayController singleton slot0')
    hour=read(instance+0x20,1,'actual timeOfDay byte from get_TimeOfDay')
    last=read(instance+0xe0,1,'klt last-applied hour cache byte')
    report=dict(captured_utc=datetime.now(timezone.utc).isoformat(),pid=PID,elf_base=hex(BASE),base_mapping=proof,
        hour=hour,last_applied_hour=last,reads=reads,total_memory_bytes_read=sum(r['bytes']for r in reads),
        source_instructions=dict(instance_getter='0x3687a50: GOT0x7212e08 -> Il2CppClass -> static_fields+0xb8 -> singleton[0]',hour_getter='0x3687cf8: ldrb w0,[x0,#0x20]; ret'),
        authorization='Root requested exact actual hour and optional last-applied byte, approved corrected +0x20 and cached +0xe0 offsets; no other heap content',
        apk_modified=False,game_fields_modified=False,instructions_modified=False,heap_scan=False,account_wallet_or_string_fields_read=False,selinux_changed=False,input_sent=False)
    target=ROOT/'output/bloody-full-port-20261028/apk-reference36-live/first-bout-time-of-day.json'
    target.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(json.dumps(report,indent=2))
if __name__=='__main__':main()
