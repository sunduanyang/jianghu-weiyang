"""Inventory and disassemble combat-related IL2CPP methods, without execution.

Input metadata comes from export_bloody_symbols.ps1. Disassembly is evidence,
not recovered C# source and not proof that a method has been semantically read.
"""
import argparse
import collections
import json
import math
import re
import struct
from pathlib import Path

import capstone
from capstone.arm64 import ARM64_OP_FP, ARM64_OP_IMM, ARM64_OP_MEM, ARM64_OP_REG
from elftools.elf.elffile import ELFFile


GROUPS = {
    'motion': 'CharacterMover CharacterAnimationController RigPoseResetter KienematicDynamicEnabler WeaponSelfColliderInitializer',
    'input': 'PlayerTouchControls PlayerGamepadControls PlayerInputHandler NetworkInputData Joystick DynamicJoystick',
    'injury': 'BodyPart ImpactInfo InternalOrgan CharacterDamageCalculator CharacterVitality StaminaDrainer KickReceiver DeathVerdict',
    'equipment': 'bt cs SourcePrefab_HandItem SourcePrefab_ArmorItem HandItemTypes ArmorSlot CharacterConfigApplier CharacterConfigNetwork CharacterConfigData CharacterConfigDataSO CharacterConfiguration CharacterSlotsController CharactersData ItemCatalog InventoryItem InventoryItemData ShopItem ShopItemData NewItemData',
    'ai': 'CharacterEnemyAI AIHandsInputRelayer AIHandAnimBundle AIOpponentSpawner',
    'rounds': 'GameWorld LocalGameController LocalGameState NetworkedGameController NetworkedGameState MatchRules MatchRulesCampaign MatchRulesFriendly MatchRulesPractice MatchRulesRandom MatchRulesRanked MatchRulesSurvival RoundEndHandler MatchEndHandler MatchEndReason DestroyIfFallen',
    'network': 'CustomNetworkRigidbodyPosition2D CustomNetworkTransforms2D NetworkPlayer',
    'feedback': 'ImpactSFX FootstepsSFX VoiceSFX CharacterExpressions CharacterHUD CharacterImpactFeed ImpactEvent DamageDecalPainter VFXAndDecalManager FeedbackManager CameraTargetPositioner OffsetXByVelocityX CameraLensModifier ShadowMover BloodOnTheGroundCustomizer NoBloodModeToggle MatchHUD PlayerStatsBar CountdownView RoundEndView MatchEndView',
    'special': 'Cannonball HandCannon HandCannonSync',
}


def run(args):
    args.output.mkdir(parents=True, exist_ok=True)
    catalog = json.loads(args.metadata.read_text(encoding='utf-8-sig'))
    types = {t['type']: t for t in catalog['types']}
    group_by_root = {t: group for group, names in GROUPS.items() for t in names.split()}
    selected = [t for t in types.values() if t['type'].split('/')[0] in group_by_root]
    with args.binary.open('rb') as stream:
        elf = ELFFile(stream)
        segments = [dict(s.header) for s in elf.iter_segments() if s['p_type'] == 'PT_LOAD']
    raw = args.binary.read_bytes()

    def file_to_va(offset):
        for seg in segments:
            if seg['p_offset'] <= offset < seg['p_offset'] + seg['p_filesz']:
                return offset - seg['p_offset'] + seg['p_vaddr']
        raise ValueError(f'Unmapped file offset {offset:x}')

    methods_by_address = collections.defaultdict(list)
    for t in types.values():
        for method in t['methods']:
            attr = method['attributes']
            if attr.get('RVA'):
                methods_by_address[file_to_va(int(attr.get('Offset', attr['RVA']), 16))].append(method['signature'])
    if args.api_symbols:
        for m in json.loads(args.api_symbols.read_text(encoding='utf-8-sig')):
            if m.get('RVA'):
                text = m['type'] + '.' + m['name']
                address = file_to_va(int(m.get('Offset',m['RVA']), 16))
                if not any(text in s for s in methods_by_address[address]):
                    methods_by_address[address].append(text)
    def read(address, length):
        for seg in segments:
            if seg['p_vaddr'] <= address and address + length <= seg['p_vaddr'] + seg['p_filesz']:
                offset = address - seg['p_vaddr'] + seg['p_offset']
                return raw[offset:offset + length]
        return b''

    fields = {}
    for name, t in types.items():
        mapping = {}
        for f in t['fields']:
            if f['offset'].get('Offset'):
                mapping[int(f['offset']['Offset'], 16)] = f
        fields[name] = mapping
    decoder = capstone.Cs(capstone.CS_ARCH_ARM64, capstone.CS_MODE_LITTLE_ENDIAN)
    decoder.detail = True
    ledger = []
    summaries = []
    for t in selected:
        group = group_by_root[t['type'].split('/')[0]]
        lines = []
        brief = []
        for method in t['methods']:
            attr = method['attributes']
            row = dict(type=t['type'], group=group, name=method['name'], signature=method['signature'],
                       rva=attr.get('RVA'), length=attr.get('Length'), calls=[], reads=[], writes=[],
                       floats=[], instruction_count=0, semantic_review='not-yet-marked')
            ledger.append(row)
            if not attr.get('RVA') or not attr.get('Length'):
                continue
            offset, length = int(attr.get('Offset',attr['RVA']), 16), int(attr['Length'], 16)
            address = file_to_va(offset)
            row.update(file_offset=hex(offset),va=hex(address))
            instructions = list(decoder.disasm(read(address, length), address))
            row['instruction_count'] = len(instructions)
            title = '\n### ' + method['signature'] + ' VA ' + hex(address) + ' FILE ' + hex(offset)
            lines.append(title)
            brief.append(title)
            typed = {'x0': t['type']} if not method['static'] else {}
            pages, integers = {}, {}
            for op in instructions:
                notes = []
                operands = op.operands
                if op.mnemonic in ('b', 'bl') and operands and operands[0].type == ARM64_OP_IMM:
                    target = operands[0].imm
                    names = methods_by_address.get(target, [])
                    if names:
                        notes.append('CALL ' + ' | '.join(names[:4]) + (f' (+{len(names)-4} aliases)' if len(names) > 4 else ''))
                        row['calls'].extend(names)
                memory = next((v for v in operands if v.type == ARM64_OP_MEM), None)
                field = None
                if memory:
                    base = op.reg_name(memory.mem.base)
                    if base in typed:
                        field = fields.get(typed[base], {}).get(memory.mem.disp)
                        if field:
                            label = typed[base] + '.' + field['name']
                            notes.append('FIELD ' + label)
                            row['writes' if op.mnemonic.startswith('st') else 'reads'].append(label)
                    if base in pages and not memory.mem.index and operands[0].type == ARM64_OP_REG:
                        dest = op.reg_name(operands[0].reg)
                        if dest.startswith(('s', 'd')):
                            size = 4 if dest.startswith('s') else 8
                            value_bytes = read(pages[base] + memory.mem.disp, size)
                            if len(value_bytes) == size:
                                value = struct.unpack('<f' if size == 4 else '<d', value_bytes)[0]
                                if math.isfinite(value) and (value == 0 or 1e-7 <= abs(value) <= 1e7):
                                    notes.append(f'POOL_F{size*8} {value:.9g}')
                                    row['floats'].append(value)
                                if size == 8:
                                    # ARM64 d-register loads may feed two-lane
                                    # float32 SIMD arithmetic, not float64.
                                    lanes = struct.unpack('<2f', value_bytes)
                                    if all(math.isfinite(v) for v in lanes):
                                        notes.append('POOL_2F32 ' + ','.join(f'{v:.9g}' for v in lanes))
                if op.mnemonic == 'adrp' and len(operands) == 2:
                    pages[op.reg_name(operands[0].reg)] = operands[1].imm
                if op.mnemonic in ('mov', 'movz') and len(operands) == 2:
                    dest = op.reg_name(operands[0].reg)
                    if operands[1].type == ARM64_OP_IMM:
                        integers[dest] = operands[1].imm
                    if operands[1].type == ARM64_OP_REG:
                        source = op.reg_name(operands[1].reg)
                        if source in typed:
                            typed[dest] = typed[source]
                        else:
                            typed.pop(dest, None)
                if op.mnemonic.startswith('ldr') and operands and operands[0].type == ARM64_OP_REG:
                    dest = op.reg_name(operands[0].reg)
                    if dest.startswith('x'):
                        if field and field['type'] in types:
                            typed[dest] = field['type']
                        else:
                            typed.pop(dest, None)
                        pages.pop(dest, None)
                if op.mnemonic == 'fmov' and len(operands) == 2:
                    if operands[1].type == ARM64_OP_FP:
                        row['floats'].append(operands[1].fp)
                        notes.append(f'LITERAL {operands[1].fp:.9g}')
                    elif operands[1].type == ARM64_OP_IMM:
                        row['floats'].append(operands[1].imm)
                    elif operands[1].type == ARM64_OP_REG:
                        source = op.reg_name(operands[1].reg)
                        if source in integers and op.reg_name(operands[0].reg).startswith('s'):
                            bits = integers[source] & 0xffffffff
                            value = struct.unpack('<f', struct.pack('<I', bits))[0]
                            if math.isfinite(value) and (value == 0 or 1e-7 <= abs(value) <= 1e7):
                                notes.append(f'BITS_FLOAT {value:.9g}')
                                row['floats'].append(value)
                text = f'{op.address:x}: {op.mnemonic} {op.op_str}' + (' ; ' + '; '.join(notes) if notes else '')
                lines.append(text)
                if notes or op.mnemonic.startswith(('fadd', 'fsub', 'fmul', 'fdiv', 'fcmp', 'fmin', 'fmax', 'fsqrt')):
                    brief.append(text)
                if op.mnemonic == 'bl':
                    for reg in [f'x{i}' for i in range(19)]:
                        typed.pop(reg, None)
            for key in ('calls', 'reads', 'writes', 'floats'):
                row[key] = list(dict.fromkeys(row[key]))
        filename = re.sub(r'[<>:"\\|?*`]', '_', t['type'].replace('/', '__'))
        (args.output / (filename + '.asm')).write_text('\n'.join(lines), encoding='utf-8')
        (args.output / (filename + '.brief')).write_text('\n'.join(brief), encoding='utf-8')
        summaries.append(dict(type=t['type'],group=group,methods=len(t['methods']),
                              fields=t['fields'],asm=filename+'.asm',brief=filename+'.brief'))
    result = dict(scope=GROUPS,types=summaries,methods=ledger,
                  caveats=['Thin listings omit instructions; full listings are authoritative.',
                           'Supplied Cpp2IL AddressAttribute RVA equals file offset; ELF VA is computed from PT_LOAD.',
                           'Call targets can alias. A symbol is not proof of semantics.',
                           'Register/field notes are heuristic, omit inherited fields and are not a control-flow/data-flow proof.',
                           'Serialized values can be overridden during equipment or network setup.',
                           'Every generated row starts unreviewed; disassembly is not a review.'])
    (args.output / 'method-ledger.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    counts = collections.Counter(r['group'] for r in ledger)
    print(json.dumps(dict(types=len(selected),methods=len(ledger),instructions=sum(r['instruction_count'] for r in ledger),groups=counts),ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('metadata', 'binary', 'output'):
        parser.add_argument('--'+name,type=Path,required=True)
    parser.add_argument('--api-symbols',type=Path)
    run(parser.parse_args())
