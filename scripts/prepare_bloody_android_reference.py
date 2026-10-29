"""Cache official Android tools/image for running the original APK in an AVD."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request
from urllib.parse import urljoin
import xml.etree.ElementTree as ET
import zipfile

ROOT=Path(__file__).resolve().parents[1]
TOOLS=ROOT/'output/bloody-full-port-20261028/tools/android-reference'

def sha1(path):
    digest=hashlib.sha1()
    with path.open('rb')as stream:
        while chunk:=stream.read(1024*1024):digest.update(chunk)
    return digest.hexdigest()

def child(element,name):
    return next((x for x in element if x.tag.rsplit('}',1)[-1]==name),None)

def value(element,name,default=''):
    found=child(element,name)
    return found.text.strip() if found is not None and found.text else default

def packages(url,filename):
    path=TOOLS/filename
    if not path.exists():path.write_bytes(urllib.request.urlopen(url,timeout=40).read())
    return ET.fromstring(path.read_bytes())

def select(document,path,base):
    for node in document.iter():
        if node.tag.rsplit('}',1)[-1]!='remotePackage' or node.attrib.get('path')!=path:continue
        channel=child(node,'channelRef')
        if channel is not None and channel.attrib.get('ref')!='channel-0':continue
        for archive in child(node,'archives'):
            if value(archive,'host-os') not in ['','windows']:continue
            complete=child(archive,'complete')
            return dict(package=path,url=urljoin(base,value(complete,'url')),size=int(value(complete,'size')),sha1=value(complete,'checksum'))
    raise ValueError('Official package not found: '+path)

def main(download):
    TOOLS.mkdir(parents=True,exist_ok=True)
    base='https://dl.google.com/android/repository/'
    repository=packages(base+'repository2-1.xml','repository2-1.xml')
    imagebase=base+'sys-img/google_apis/'
    images=packages(imagebase+'sys-img2-1.xml','sys-img2-1.xml')
    selected=[select(repository,name,base)for name in ['platform-tools','emulator']]
    selected.append(select(images,'system-images;android-30;google_apis;x86_64',imagebase))
    (TOOLS/'official-packages.json').write_text(json.dumps(selected,indent=2),encoding='utf-8')
    print(json.dumps(selected),flush=True)
    if not download:return
    sdk=TOOLS/'sdk'
    for row in selected:
        archive=TOOLS/row['url'].rsplit('/',1)[-1]
        if not archive.exists() or sha1(archive)!=row['sha1']:
            partial=archive.with_suffix('.partial');total=partial.stat().st_size if partial.exists()else 0
            while total<row['size']:
                end=min(total+64*1024*1024-1,row['size']-1)
                request=urllib.request.Request(row['url']+'?reference_range='+str(total),headers={'Range':f'bytes={total}-{end}','Accept-Encoding':'identity'})
                previous=total
                with urllib.request.urlopen(request,timeout=90)as response,partial.open('ab')as out:
                    content_range=response.headers.get('Content-Range','')
                    if response.status!=206 or not content_range.startswith(f'bytes {total}-'):raise ValueError('Unexpected package byte range '+content_range)
                    while chunk:=response.read(1024*1024):out.write(chunk);total+=len(chunk)
                if total<=previous:raise ValueError('Package download made no progress')
                print('DOWNLOAD',row['package'],total,row['size'],flush=True)
            if sha1(partial)!=row['sha1'] or total!=row['size']:raise ValueError('Official checksum/size failed: '+row['package'])
            partial.replace(archive)
        destination=sdk
        if row['package'].startswith('system-images;'):destination=sdk/'system-images/android-30/google_apis'
        destination.mkdir(parents=True,exist_ok=True)
        with zipfile.ZipFile(archive)as z:
            for entry in z.infolist():
                target=destination/entry.filename
                if not target.resolve().is_relative_to(destination.resolve()):raise ValueError('Archive path escape')
                if entry.is_dir():target.mkdir(parents=True,exist_ok=True)
                else:
                    target.parent.mkdir(parents=True,exist_ok=True)
                    with z.open(entry)as source,target.open('wb')as out:
                        import shutil
                        shutil.copyfileobj(source,out)
        print('EXTRACTED',row['package'],flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--download',action='store_true');main(parser.parse_args().download)
