"""Authorized 28-byte hq static pointer chain + WeatherType read only."""
from datetime import datetime, timezone
import json
import re
import struct
import subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
ADB='D:/codex-bloody-workspace-20261028/output/bloody-full-port-20261028/tools/android-reference/sdk/platform-tools/adb.exe'
PREFIX=[ADB,'-P','5051','-s','emulator-5586']
PID=2334
BASE=0x75ca06301000


def run(*args):
    return subprocess.run(PREFIX+list(args),capture_output=True,timeout=20)


def main():
    metadata=json.loads((ROOT/'output/bloody-code-audit-20261028/game-metadata.json').read_text(encoding='utf-8'))
    hq=next(t for t in metadata['types'] if t['type']=='hq')
    fields=[f for f in hq['fields'] if f['static'] and f['type']=='WeatherType']
    assert len(fields)==1 and fields[0]['name']=='ldx'
    enum=next(t for t in metadata['types'] if t['type']=='WeatherType')
    names={f['constant']:f['name'] for f in enum['fields'] if f['static']}
    if run('shell','getprop','ro.boot.qemu.avd_name').stdout.strip()!=b'bloody-reference36':
        raise RuntimeError('Dedicated AVD identity changed')
    if run('shell','pidof','com.tibith.badboxing').stdout.strip()!=b'2334':
        raise RuntimeError('Original reference PID changed; no reads attempted')
    result=run('shell','cat','/proc/2334/maps')
    if result.returncode:raise RuntimeError('Source maps refused')
    lines=result.stdout.decode().splitlines()
    proof=next(line for line in lines if line.startswith(f'{BASE:x}-') and 'libil2cpp.so' in line and line.split()[2]=='00000000')
    ranges=[]
    for line in lines:
        match=re.match(r'([0-9a-f]+)-([0-9a-f]+)\s+(\S+)',line)
        if match and 'r' in match[3]:ranges.append((int(match[1],16),int(match[2],16)))
    reads=[]
    def read(address,size,label):
        if address%size or not any(low<=address and address+size<=high for low,high in ranges):
            raise RuntimeError('Authorized address not readable: '+label)
        response=run('exec-out','dd','if=/proc/2334/mem',f'bs={size}',f'skip={address//size}','count=1','status=none')
        if response.returncode or len(response.stdout)!=size:raise RuntimeError('Authorized read refused: '+label)
        value=struct.unpack('<Q' if size==8 else '<i',response.stdout)[0]
        reads.append(dict(label=label,address=hex(address),bytes=size,value=hex(value) if size==8 else value))
        return value
    variable=read(BASE+0x7212238,8,'hq TypeInfo GOT')
    klass=read(variable,8,'hq Il2CppClass pointer variable')
    static=read(klass+0xb8,8,'hq Il2CppClass.static_fields')
    weather=read(static+0x10c,4,'hq.ldx WeatherType / original CurrentWeatherType')
    report=dict(captured_utc=datetime.now(timezone.utc).isoformat(),pid=PID,elf_base=hex(BASE),base_mapping=proof,
        weather_type=weather,weather_name=names.get(weather,'invalid enum value'),reads=reads,total_memory_bytes_read=28,
        metadata_field=fields[0],source_store='WeatherManager.ChangeWeatherType VA0x364226c reads self+0x58 CurrentWeatherType and stores into hq.static_fields+0x10c',
        source_reader='WeatherManager.crk VA0x3642368 and crl VA0x3642470 read hq.static_fields+0x10c as previous WeatherType',
        authorization='Parent explicitly requested original first-bout weather only after metadata confirmation',
        apk_modified=False,game_fields_modified=False,instructions_modified=False,heap_scan=False,
        account_wallet_or_string_fields_read=False,selinux_changed=False,input_sent=False)
    target=ROOT/'output/bloody-full-port-20261028/apk-reference36-live/first-bout-weather.json'
    target.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,indent=2))


if __name__=='__main__':main()
