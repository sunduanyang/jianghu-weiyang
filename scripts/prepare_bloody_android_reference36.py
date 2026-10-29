"""Download the one authorized official API36 candidate beside API30.

Does not edit the original package manifest, an AVD, a global environment
setting, or any image contents. The archive is strictly size/SHA1 checked.
"""
import argparse
import json
from pathlib import Path
import shutil
import time
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from prepare_bloody_android_reference import TOOLS,select,sha1

EXPECTED=dict(package='system-images;android-36;google_apis;x86_64',
    url='https://dl.google.com/android/repository/sys-img/google_apis/x86_64-36_r07.zip',
    size=1895447397,sha1='c6bf44bdcd885bb902b4ba752d111a073ad7a817')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--download',action='store_true')
    args=parser.parse_args()
    official=select(ET.fromstring((TOOLS/'sys-img2-1.xml').read_bytes()),
        EXPECTED['package'],'https://dl.google.com/android/repository/sys-img/google_apis/')
    if official!=EXPECTED:raise ValueError('Pinned official API36 package metadata differs')
    manifest=TOOLS/'official-package-api36.json'
    manifest.write_text(json.dumps(dict(official=official,verified=False,extracted=False),indent=2),encoding='utf-8')
    print(json.dumps(official),flush=True)
    if not args.download:return
    archive=TOOLS/'x86_64-36_r07.zip';partial=archive.with_suffix('.partial')
    verified=archive.exists()and archive.stat().st_size==EXPECTED['size']and sha1(archive)==EXPECTED['sha1']
    if not verified:
        failures=0
        while (partial.stat().st_size if partial.exists()else 0)<EXPECTED['size']:
            start=partial.stat().st_size if partial.exists()else 0
            end=min(start+16*1024*1024-1,EXPECTED['size']-1)
            request=urllib.request.Request(EXPECTED['url']+'?reference36_range='+str(start),
                headers={'Range':f'bytes={start}-{end}','Accept-Encoding':'identity'})
            try:
                with urllib.request.urlopen(request,timeout=45)as response:
                    expected=f'bytes {start}-{end}/{EXPECTED["size"]}'
                    if response.status!=206 or response.headers.get('Content-Range')!=expected:
                        raise ValueError('Unexpected official response range '+str(response.headers.get('Content-Range')))
                    with partial.open('ab')as out:
                        count=0
                        while chunk:=response.read(1024*1024):
                            count+=len(chunk)
                            if count>end-start+1:raise ValueError('Unexpected excess bytes in range')
                            out.write(chunk)
                        if count!=end-start+1:raise ValueError('Short range download')
                failures=0
                print(f'DOWNLOAD {partial.stat().st_size}/{EXPECTED["size"]}',flush=True)
            except Exception as error:
                failures+=1;print(f'RETRY {failures}: {type(error).__name__}: {error}',flush=True)
                if failures>=5:raise
                time.sleep(1)
        if partial.stat().st_size!=EXPECTED['size']or sha1(partial)!=EXPECTED['sha1']:
            raise ValueError('Official API36 size/SHA1 failed')
        partial.replace(archive)
    manifest.write_text(json.dumps(dict(official=official,verified=True,extracted=False),indent=2),encoding='utf-8')
    print('OFFICIAL_SIZE_SHA1_VERIFIED',flush=True)
    destination=TOOLS/'sdk/system-images/android-36/google_apis'
    destination.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(archive)as source:
        for entry in source.infolist():
            target=destination/entry.filename
            if not target.resolve().is_relative_to(destination.resolve()):raise ValueError('Archive path escape')
            if entry.is_dir():target.mkdir(parents=True,exist_ok=True)
            else:
                target.parent.mkdir(parents=True,exist_ok=True)
                with source.open(entry)as inp,target.open('wb')as out:shutil.copyfileobj(inp,out)
    manifest.write_text(json.dumps(dict(official=official,verified=True,extracted=True,destination=str(destination)),indent=2),encoding='utf-8')
    print('EXTRACTED_API36',flush=True)


if __name__=='__main__':main()
