"""Read-only camera audit: original ARM64 target selection and blend lookup.

No production camera, source data, Unity project, APK or running game is edited.
Unity object/delegate/string calls are bounded observation hooks; ccr,
FocusCamerasOnWinningPlayer and GetBlendForVirtualCameras execute original code.
"""
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X2, UC_ARM64_REG_X3, UC_ARM64_REG_X4, UC_ARM64_REG_LR, UC_ARM64_REG_PC

OUT = ROOT / 'output/bloody-full-port-20261028/camera-review'
VISUAL = json.loads((ROOT / 'output/bloody-full-port-20261028/original-visual-catalog.json').read_text(encoding='utf-8'))


class CameraOracle(NativeAI):
    def __init__(self):
        self.strings = {}; self.string_ids = {}; self.events = []; self.opponent = 0x10014000
        super().__init__()
        self.p64(0x72639F8, 0x1001F000)
        self.p64(0x1001F000, self.string('**ANY CAMERA**'))
        for i, (cell, value) in enumerate([(0x720D1D8, 0), (0x72120F8, 0), (0x7212368, 0x10012000), (0x7212370, 0x10013000)]):
            pointer = 0x10016000 + i * 0x800; klass = pointer + 0x100; statics = pointer + 0x400
            self.p64(cell, pointer); self.p64(pointer, klass); self.p32(klass + 0xE4, 1)
            self.p64(klass + 0xB8, statics); self.p64(statics, value)
        self.p64(0x720D168, 0x10016000); self.p64(0x7212378, 0x10016000)
        self.p64(0x7212238, 0x1001E000); self.p64(0x1001E000, 0x1001E100)
        self.p32(0x1001E100 + 0xE4, 1); self.p64(0x1001E100 + 0xB8, 0x1001E400)
        for player, slot in [(0x10013000, 0), (0x10014000, 1)]:
            self.p32(player + 0x68, slot)
            self.p64(player + 0x80, 0x20000 + slot * 0x100 + 1)
            self.p64(player + 0x88, 0x20000 + slot * 0x100 + 2)

    def p64(self, p, v): self.cpu.mem_write(p, struct.pack('<Q', v))
    def p32(self, p, v): self.cpu.mem_write(p, struct.pack('<I', v))
    def string(self, s):
        if s is None: return 0
        if s not in self.string_ids:
            p = 0x1001C000 + len(self.string_ids) * 8
            self.strings[p] = s; self.string_ids[s] = p
        return self.string_ids[s]

    def _hook(self, cpu, address, size, data):
        x0 = cpu.reg_read(UC_ARM64_REG_X0); x1 = cpu.reg_read(UC_ARM64_REG_X1)
        if address == 0x5B7A820: value = int(self.strings.get(x0) == self.strings.get(x1))
        elif address == 0x5B874B4: value = int(not self.strings.get(x0))
        elif address == 0x6ACBD7C: value = int(x0 == x1)
        elif address == 0x364D0B8: value = 0
        elif address == 0x365D904: value = self.opponent
        elif address == 0x365B424: value = 0x10013000 if x1 == 0 else 0x10014000
        elif address == 0x34159AC:
            self.events.append({'set_follow_and_look_at': hex(x1)}); value = 0
        elif address == 0x3591B14: value = self.string('FocusCamerasOnWinningPlayer')
        elif address == 0x6ACC03C:
            self.events.append({'invoke': self.strings[x1], 'scaled_delay': self.sf(0)}); value = 0
        elif address == 0x316B7CC: value = 0
        elif address == 0x316BB40: value = 0x1001D000
        elif address == 0x5C6FF00: value = 0
        elif address == 0x350CA10:
            self.events.append({'subscribe': 'CharacterVitality.OnDeathEvent'}); value = 0
        elif address == 0x366A28C: value = 1
        elif address == 0x3415C94:
            self.events.append({'close_network_ui': x1}); value = 0
        elif address == 0x3415E60:
            self.events.append({'open_network_ui': x1, 'delayed': bool(cpu.reg_read(UC_ARM64_REG_X2))}); value = 0
        elif address == 0x3415FF0:
            self.events.append({'activate_camera': True}); value = 0
        elif address == 0x3416458:
            self.events.append({'refresh_top_bar_modules': True}); value = 0
        elif address == 0x63EA098:
            self.events.append({'CinemachineBrain.ResetState': True}); value = 0
        else:
            return super()._hook(cpu, address, size, data)
        cpu.reg_write(UC_ARM64_REG_X0, value)
        cpu.reg_write(UC_ARM64_REG_PC, cpu.reg_read(UC_ARM64_REG_LR))

    def blend(self, entries, incoming, outgoing):
        # Native struct ABI: packed Style+Time in x3, CustomCurve pointer x4;
        # result occupies x0/x1. Distinct ids preserve selected-entry identity.
        self.p64(self.SELF + 0x18, 0x10018000)
        self.p64(0x10018000 + 0x18, len(entries))
        for i, entry in enumerate(entries):
            self.cpu.mem_write(0x10018020 + i * 32, struct.pack('<QQQQ', self.string(entry['From']), self.string(entry['To']), i + 1, 0))
        self.cpu.reg_write(UC_ARM64_REG_X2, self.string(incoming))
        self.cpu.reg_write(UC_ARM64_REG_X3, 0); self.cpu.reg_write(UC_ARM64_REG_X4, 0)
        self.call(0x6413B90, self.SELF, self.string(outgoing))
        return self.cpu.reg_read(UC_ARM64_REG_X0) - 1

    def verdict(self, won, round_end, has_opponent=True):
        self.cpu.mem_write(self.SELF, bytes(0x100)); self.events = []
        self.opponent = 0x10014000 if has_opponent else 0
        self.cpu.reg_write(UC_ARM64_REG_X2, int(round_end))
        self.call(0x34156CC, self.SELF, int(won))
        return list(self.events)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    native = CameraOracle(); cases = []; errors = []
    for won in [False, True]:
        for round_end in [False, True]:
            for opponent in [False, True]:
                actual = native.verdict(won, round_end, opponent)
                expected = [{'set_follow_and_look_at': '0x20002'}] if not opponent else [{'set_follow_and_look_at': '0x20101' if won else '0x20001'}]
                if opponent and (won or round_end): expected.append({'invoke': 'FocusCamerasOnWinningPlayer', 'scaled_delay': .625})
                cases.append(dict(kind='native_verdict', won=won, round_end=round_end, opponent=opponent, actual=actual, expected=expected))
                if actual != expected: errors.append(cases[-1])
    for winner, target in [(0, None), (0x10013000, '0x20002'), (0x10014000, '0x20102')]:
        native.cpu.mem_write(native.SELF, bytes(0x100)); native.p64(native.SELF + 0x80, winner); native.events = []
        native.call(0x341591C, native.SELF)
        expected = [] if target is None else [{'set_follow_and_look_at': target}]
        cases.append(dict(kind='native_focus_winner', winner=hex(winner), actual=list(native.events), expected=expected))
        if native.events != expected: errors.append(cases[-1])
    native.cpu.mem_write(native.SELF, bytes(0x100)); native.p64(native.SELF + 0x28, 0x10012000); native.events = []
    native.call(0x341B974, native.SELF)
    expected = [{'subscribe': 'CharacterVitality.OnDeathEvent'}]
    cases.append(dict(kind='native_target_on_enable', actual=list(native.events), expected=expected))
    if native.events != expected: errors.append(cases[-1])
    for old in [0, 2, 3, 4]:
        for new in [2, 3, 4]:
            for mode in [0, 2, 5]:
                native.cpu.mem_write(native.SELF, bytes(0x100)); native.p32(native.SELF + 0x60, old)
                native.p64(native.SELF + 0x58, 0x10012000); native.p32(0x1001E494, mode)
                native.events = []; native.cpu.reg_write(UC_ARM64_REG_X2, new)
                native.call(0x3415AC8, native.SELF, old)
                expected = [{'close_network_ui': old}, {'open_network_ui': new, 'delayed': old == 2 and new == 4}, {'activate_camera': True}, {'refresh_top_bar_modules': True}]
                if new == 4 and mode == 2: expected.append({'CinemachineBrain.ResetState': True})
                cases.append(dict(kind='native_network_state_camera', old=old, new=new, mode=mode, actual=list(native.events), expected=expected))
                if native.events != expected: errors.append(cases[-1])
    blends = next(r['data']['CustomBlends'] for r in VISUAL['scene'] if r['script'] == 'CinemachineBlenderSettings')
    names = sorted(set([b[k] for b in blends for k in ['From','To'] if b[k] != '**ANY CAMERA**']))
    mismatches = []
    for outgoing in names:
        for incoming in names:
            if outgoing == incoming: continue
            actual = native.blend(blends, incoming, outgoing)
            # Verified ARM precedence, with first occurrence retained for each
            # specific category; unrestricted ANY/ANY uses last occurrence.
            exact = [i for i,b in enumerate(blends) if b['From']==outgoing and b['To']==incoming]
            to = [i for i,b in enumerate(blends) if b['From']=='**ANY CAMERA**' and b['To']==incoming]
            fr = [i for i,b in enumerate(blends) if b['From']==outgoing and b['To']=='**ANY CAMERA**']
            any_pair = [i for i,b in enumerate(blends) if b['From']=='**ANY CAMERA**' and b['To']=='**ANY CAMERA**']
            expected = exact[0] if exact else to[0] if to else fr[0] if fr else any_pair[-1] if any_pair else -1
            cases.append(dict(kind='native_blend_lookup', outgoing=outgoing, incoming=incoming, actual=actual, expected=expected))
            if actual != expected: errors.append(cases[-1])
            current = -1
            for i,b in enumerate(blends):
                if b['From'] in [outgoing,'**ANY CAMERA**'] and b['To'] in [incoming,'**ANY CAMERA**']: current=i
            if current != actual: mismatches.append(dict(outgoing=outgoing, incoming=incoming, source=blends[actual], current=blends[current]))
    result = dict(scope='Read-only original ARM64 camera selection audit; no camera runtime, screenshot parity or process stability claim', native_cases=len(cases), failures=errors, current_lookup_mismatches=mismatches, cases=cases)
    (OUT / 'native-camera-audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(native_cases=len(cases), failures=len(errors), current_lookup_mismatches=len(mismatches), report=str(OUT/'native-camera-audit.json'))))
    if errors: raise SystemExit(1)


if __name__ == '__main__': main()
