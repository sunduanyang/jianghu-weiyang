"""Scope web-only networking to this task's empty, dedicated Android guest.

No host firewall/network settings are changed. Private destinations, including
the emulator host alias 10.0.2.2, are denied before public TCP 80/443. Only the
emulator's exact DNS proxy may use private-address UDP/TCP 53. IPv6 stays denied.
"""
import argparse
from datetime import datetime,timezone
import json
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parents[1]
BASE=Path('D:/codex-bloody-workspace-20261028/output/bloody-full-port-20261028/tools/android-reference')
ADB=BASE/'sdk/platform-tools/adb.exe'
PREFIX=[str(ADB),'-P','5051','-s','emulator-5586','shell']
CHAIN='BLOODY_REF36'


def run(*args,check=True):
    result=subprocess.run(PREFIX+list(args),capture_output=True,text=True,
        encoding='utf-8',errors='replace',timeout=20)
    if check and result.returncode:raise RuntimeError(str(args)+': '+result.stderr+result.stdout)
    return dict(command=list(args),exit_code=result.returncode,
        stdout=result.stdout.strip(),stderr=result.stderr.strip())


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--allow-public-web',action='store_true')
    parser.add_argument('--use-existing-proxy',action='store_true',
        help='Root-authorized exact guest exception to existing host HTTP proxy port18780')
    args=parser.parse_args()
    identity={key:run('getprop',key)['stdout']for key in [
        'ro.build.version.sdk','ro.boot.qemu.avd_name','ro.build.fingerprint']}
    if identity['ro.build.version.sdk']!='36' or identity['ro.boot.qemu.avd_name']!='bloody-reference36':
        raise RuntimeError('Not the dedicated reference36 guest: '+str(identity))
    if not args.allow_public_web:
        print(json.dumps(dict(identity=identity,rules=run('iptables','-S','OUTPUT')),indent=2));return
    existing=run('iptables','-S',CHAIN,check=False)
    if existing['exit_code']==0:run('iptables','-F',CHAIN)
    else:run('iptables','-N',CHAIN)
    run('iptables','-A',CHAIN,'-o','lo','-j','ACCEPT')
    for protocol in ['udp','tcp']:
        run('iptables','-A',CHAIN,'-d','10.0.2.3/32','-p',protocol,'--dport','53','-j','ACCEPT')
    if args.use_existing_proxy:
        run('iptables','-A',CHAIN,'-d','10.0.2.2/32','-p','tcp','--dport','18780','-j','ACCEPT')
    private=[
        '0.0.0.0/8','10.0.0.0/8','100.64.0.0/10','127.0.0.0/8',
        '169.254.0.0/16','172.16.0.0/12','192.168.0.0/16',
        '192.0.0.0/24','192.0.2.0/24','198.18.0.0/15',
        '198.51.100.0/24','203.0.113.0/24','224.0.0.0/4','240.0.0.0/4']
    for subnet in private:run('iptables','-A',CHAIN,'-d',subnet,'-j','DROP')
    run('iptables','-A',CHAIN,'-p','tcp','-m','multiport','--dports','80,443','-j','ACCEPT')
    run('iptables','-A',CHAIN,'-j','DROP')
    if run('iptables','-C','OUTPUT','-j',CHAIN,check=False)['exit_code']:
        run('iptables','-I','OUTPUT','1','-j',CHAIN)
    run('cmd','connectivity','airplane-mode','disable')
    run('svc','wifi','enable')
    # Cellular remains disabled, and the previous guest IPv6 DROP is retained.
    run('svc','data','disable')
    if args.use_existing_proxy:
        run('settings','put','global','http_proxy','10.0.2.2:18780')
    report=dict(captured_utc=datetime.now(timezone.utc).isoformat(),identity=identity,
        adb_server_port=5051,device='emulator-5586',
        authorization='Root authorized public TCP80/443 plus necessary DNS for unmodified original APK reference; no personal accounts, purchases, messages or host file uploads.',
        existing_proxy_exception='TCP 10.0.2.2:18780 only; Root verified this already-enabled host proxy can reach public game services without credentials.'if args.use_existing_proxy else None,
        guest_http_proxy=run('settings','get','global','http_proxy'),
        ipv4_chain=run('iptables','-S',CHAIN),ipv4_output=run('iptables','-S','OUTPUT'),
        ipv6_output=run('ip6tables','-S','OUTPUT'),
        host_firewall_changed=False,apk_modified=False,image_instruction_modified=False)
    path=ROOT/('output/bloody-full-port-20261028/apk-reference36-live/network-public-web-proxy.json'if args.use_existing_proxy else 'output/bloody-full-port-20261028/apk-reference36-live/network-public-web.json')
    path.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(dict(path=str(path),identity=identity,private_destinations_blocked=True,
        public_allowed='TCP80/443',dns='10.0.2.3 UDP/TCP53',ipv6='blocked',
        proxy_exception='10.0.2.2:18780 TCP'if args.use_existing_proxy else None),indent=2))


if __name__=='__main__':main()
