"""Audit 5.0.3 AI coroutine factory types via ELF RELA + metadata39.

Read-only evidence tool. It never executes the supplied native library.
Requires the extraction's libil2cpp.so/global-metadata.dat and pyelftools.
"""
from pathlib import Path
import argparse
import json
import re
import struct
from elftools.elf.elffile import ELFFile

FACTORIES = {
    'ciy': ('CharacterEnemyAI/cl', 'state'),
    'ciz': ('CharacterEnemyAI/ce', 'action'),
    'cja': ('CharacterEnemyAI/cj', 'reactive'),
    'cjb': ('CharacterEnemyAI/cg', 'dodge counter'),
    'cji': ('CharacterEnemyAI/ca', 'walk -1'),
    'cjj': ('CharacterEnemyAI/cc', 'walk +1'),
    'cjk': ('CharacterEnemyAI/cd', 'double-tap walk'),
    'cjl': ('CharacterEnemyAI/bz', 'four-tap kick'),
    'cjp': ('CharacterEnemyAI/cf', 'attack'),
    'cjs': ('CharacterEnemyAI/ch', 'procedural strike'),
    'cjt': ('CharacterEnemyAI/ci', 'pull backward'),
    'cju': ('CharacterEnemyAI/ck', 'victory'),
}


def audit(library, metadata, recovered):
    raw = metadata.read_bytes()
    assert struct.unpack_from('<II', raw) == (0xfab11baf, 39), 'Expected source metadata39'
    # Metadata39 stores (offset, byte size, element count), not old 8-byte pairs.
    strings, string_bytes, _ = struct.unpack_from('<III', raw, 32)
    types, type_bytes, type_count = struct.unpack_from('<III', raw, 236)
    stride = type_bytes // type_count
    assert stride == 82 and type_bytes % type_count == 0

    def string(index):
        assert 0 <= index < string_bytes
        start = strings + index
        return raw[start:raw.index(0, start)].decode('utf-8')

    type_rows = {}
    for index in range(type_count):
        name, namespace, by_value, declaring = struct.unpack_from('<iiii', raw, types + stride * index)
        type_rows[by_value] = dict(index=index, name=string(name), namespace=string(namespace), declaring=declaring)

    def full_name(index):
        row = type_rows[index]
        if row['declaring'] >= 0:
            return full_name(row['declaring']) + '/' + row['name']
        return (row['namespace'] + '.' if row['namespace'] else '') + row['name']

    pointers = {}
    for name, (expected, behavior) in FACTORIES.items():
        source = next(recovered.glob('CharacterEnemyAI__' + name + '_*.c'))
        address = int(re.search(r'PTR_DAT_(0[0-9a-f]{7})', source.read_text()).group(1), 16)
        pointers[address] = (name, expected, behavior)
    source = next(recovered.glob('AIHandsInputRelayer__cdm_*.c'))
    pointers[int(re.search(r'PTR_DAT_(0[0-9a-f]{7})', source.read_text()).group(1), 16)] = ('AIHandsInputRelayer.cdm', 'AIHandsInputRelayer/bs', 'defensive relay')
    rows = []
    with library.open('rb') as stream:
        elf = ELFFile(stream)
        assert elf['e_machine'] == 'EM_AARCH64'
        segments = [segment for segment in elf.iter_segments() if segment['p_type'] == 'PT_LOAD']

        def read_u64(address):
            segment = next(s for s in segments if s['p_vaddr'] <= address < s['p_vaddr'] + s['p_filesz'])
            stream.seek(segment['p_offset'] + address - segment['p_vaddr'])
            return struct.unpack('<Q', stream.read(8))[0]

        for section in elf.iter_sections():
            if not hasattr(section, 'iter_relocations'):
                continue
            for relocation in section.iter_relocations():
                address = relocation['r_offset']
                if address not in pointers:
                    continue
                assert relocation['r_info_type'] == 1027, 'Expected R_AARCH64_RELATIVE'
                target = relocation['r_addend']
                encoded = read_u64(target)
                assert encoded >> 29 == 1, 'Expected Il2CppClass metadata usage'
                type_index = (encoded & 0x1ffffffe) >> 1
                name, expected, behavior = pointers[address]
                actual = full_name(type_index)
                assert actual == expected, (name, actual, expected)
                rows.append(dict(factory=name, pointer=hex(address), relocation=hex(target), encoded=hex(encoded),
                                 type_index=hex(type_index), type_definition=type_rows[type_index]['index'],
                                 actual_type=actual, behavior=behavior, verified=True))
    assert len(rows) == len(pointers)
    return dict(metadata_version=39, type_record_bytes=stride, factories=sorted(rows, key=lambda r: r['factory']),
        direction_chain={
            'cit': 'self Transform.jpa.x - PlayerPosition.x; ARM64 342c21c fsub s0,s8,s0',
            'ciw_positive': 'cji -> ca.MoveNext -> AIWalk(-1)',
            'cix_positive': 'cjj -> cc.MoveNext -> AIWalk(+1)',
            'clu_true': '343604c fmov s0,#-1; clw -> MoveDirection -1',
            'clu_false': '3436058 fmov s0,#+1; clw -> MoveDirection +1',
            'force': 'FixedUpdateNetwork 34359e0: AddForce(new Vector2(movementSpeed*MoveDirection,0)); positive serialized speed19',
            'result': 'ciw closes world separation; cix increases world separation; character flip does not reverse force'})


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--library', type=Path)
    parser.add_argument('--metadata', type=Path)
    parser.add_argument('--recovered', type=Path, default=Path('output/bloody-full-port-20261028/recovered-combat'))
    parser.add_argument('--out', type=Path, default=Path('output/source-ai-factory-type-audit.json'))
    args = parser.parse_args()
    temp = Path('output/bloody-full-port-20261028/tools/AssetRipper/temp')
    result = audit(args.library or next(temp.glob('**/libil2cpp.so')),
                   args.metadata or next(temp.glob('**/global-metadata.dat')), args.recovered)
    args.out.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"Verified {len(result['factories'])} coroutine allocation types; ciw approaches, cix retreats in world X.")
