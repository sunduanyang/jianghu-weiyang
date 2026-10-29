"""Generate the REVIEWABLE Jianghu adapter, not an original-item replacement.

No original prefab/art is copied. APK observations and Jianghu design choices
are stored in separate sections. Run with --check to detect catalog drift.
"""
import argparse
import json
from pathlib import Path
from build_bloody_item_catalog import plain_float

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'unity/JianghuUnity/Assets/Resources/CombatSource/equipment.json'
# Actual serialized component properties for explicitly named low-tier samples.
# cqe replaces their masses, but InArena=true leaves these properties intact.
HELD_SAMPLES = {
    'dagger': ('HandItem_Dagger_1', 197, .5, 2.5),
    'sabre': ('HandItem_Sword_1', 246, 0, .05),
    'sword': ('HandItem_Sword_1', 246, 0, .05),
    'axe': ('HandItem_Axe_1', 187, 0, .05),
    'mace': ('HandItem_Mace_1', 212, 0, .05),
    'spear': ('HandItem_2HandedSpear_1', 178, 10, 15),
    'greatsword': ('HandItem_2HandedSword_1', 182, 15, 25),
    'greataxe': ('HandItem_2HandedAxe_1', 168, 15, 25),
    'warhammer': ('HandItem_2HandedHammer_1', 173, 15, 25),
    'shield': ('HandItem_Shield_1', 216, 0, .05),
}


def build():
    catalog = json.loads((ROOT / 'jianghu/weapon_catalog.json').read_text(encoding='utf-8'))
    profiles = json.loads((ROOT / 'jianghu/physical_profiles.json').read_text(encoding='utf-8'))
    source = json.loads((ROOT / 'output/bloody-full-port-20261028/source-combat-data.json').read_text(encoding='utf-8'))
    nothing = next(x for x in source['items'] if x['source']==dict(file='sharedassets0.assets',id=273502))
    graph = source['item_graphs']['sharedassets0.assets']
    physics = {x['id']:x['data'] for x in graph['physics']}
    base = physics[nothing['data']['ItemBaseRigidbody2D']['m_PathID']]
    counter = physics[nothing['data']['CounterweightRigidbody2D']['m_PathID']]
    circle = physics[149137]
    held = {}
    for key, row in HELD_SAMPLES.items():
        item = next(x['data'] for x in source['items'] if x['script']=='SourcePrefab_HandItem' and x['data']['itemId']==row[1])
        held[key] = dict(sample_name=row[0], sample_item_id=row[1],
            body_gravity=1, counterweight_gravity=1, linear_damping=row[2], angular_damping=row[3],
            counterweight_linear_damping=0, counterweight_angular_damping=.05,
            impact_speed_threshold=item['impactSpeedThreshold'],clash_damage_stat=plain_float(item['obs_Damage']))
    assert nothing['data']['Name']=='Nothing' and nothing['data']['Type']==7 and nothing['data']['HandItemType']==0
    weapons = {}
    for key, row in catalog.items():
        profile = row['profile']
        head = ('WoodBlunt' if key == 'wood_sword' else 'MetalBlunt' if key == 'iron_practice_sword'
                or profile in ('mace', 'warhammer') else 'MetalSpike' if profile == 'spear' else 'MetalBlade')
        handle = 'WoodHandle' if key == 'wood_sword' or profile in ('axe', 'greataxe', 'mace', 'warhammer', 'spear') else 'MetalHandle'
        weapons[key] = dict(profile=profile, head=head, handle=handle,
                            damage_coefficient=row['damage'], effective_source_D=round(16 * row['damage'] - 15, 8),
                            mass_scale=round(row['mass'] / profiles[profile]['mass'], 8))
    return dict(schema=1, adapter='jianghu-source-equipment-v1',
        apk_sha256='c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba',
        source_observations=dict(damage_formula='weighted_speed * (1 + D / 15) * .25 * 2.15',
            special_D=.1, special_formula='weighted_speed * .25 / 2.5',
            mass_va='0x363e0d8', armor_defense_va='0x3428820', armor_material_va='0x341a75c',
            armor_materials=dict(Nothing=0, Cloth=1, Leather=2, Wood=3, Mail=4, PlateThin=5, PlateThick=6, Brigandin=7)),
        adaptation=dict(damage='effective_D = 16 * game_damage_coefficient - 15; base D=1; negative values intentionally preserve ratios',
            mass='source category masses multiplied by game mass / profile base mass',
            armor='one campaign armor slot maps to one covered layer, not three duplicated layers',
            unarmed='source Nothing numeric shape/body/counterweight; Type=7 controls COM/arm props while HandItemType=0 controls cqe masses; AI still filters Nothing',
            shield='existing wood_shield maps to ordinary source shield D=1, WoodBlunt'),
        profiles={key: dict(base_mass=row['mass'], base_damage=row['damage']) for key, row in profiles.items()},
        held_profiles=held,
        nonarena_override=dict(condition='!Application.isBatchMode && !TransitionController.InArena && !IsFlailHead',
            body_gravity=-1, counterweight_gravity=-5, linear_damping=50, angular_damping=100,
            flag_getter_va='0x36893fc', flag_static_offset=8,
            initialization_window='SpawnRunner precedes InArena=true; first equipment OnEnable timing remains unverified dynamically'),
        nothing=dict(source=nothing['source'], name='Nothing', type=7, hand_item_type=0, length=nothing['data']['Length'],impact_speed_threshold=nothing['data']['impactSpeedThreshold'],
            body_mass=1, counterweight_mass=1, body_gravity=base['m_GravityScale'], counterweight_gravity=counter['m_GravityScale'],
            linear_damping=base['m_LinearDamping'], angular_damping=base['m_AngularDamping'],
            counterweight_linear_damping=counter['m_LinearDamping'], counterweight_angular_damping=counter['m_AngularDamping'],
            body_sleep_mode=base['m_SleepingMode'],counterweight_sleep_mode=counter['m_SleepingMode'],
            body_interpolation=base['m_Interpolate'],counterweight_interpolation=counter['m_Interpolate'],
            radius=circle['m_Radius'],counterweight_radius=physics[149288]['m_Radius'],surface='WoodBlunt',
            mass_evidence='SourcePrefab_HandItem.cqe 0x363e0d8, HandItemType=0 single-handed; serialized counterweight .25 is overwritten to 1',
            slot_evidence='CharacterConfigApplier.ApplyHandItemLeft/Right Type=7: handCOMShieldMass + cez(shield=true); not a shield attack in AI'),
        weapons=weapons,
        armor={
            'cloth': dict(material=1, defense=1, coverage=['torso','belly','pelvis','upper','forearm']),
            'leather': dict(material=2, defense=4, coverage=['torso','belly','pelvis']),
            'silk_robe': dict(material=1, defense=7, coverage=['torso','belly','pelvis','upper','forearm','thigh']),
        })


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    text = json.dumps(build(), ensure_ascii=False, indent=2) + '\n'
    if args.check:
        if DEST.read_text(encoding='utf-8') != text:
            raise SystemExit('equipment.json differs: run scripts/build_duel_source_equipment.py')
    else:
        DEST.write_text(text, encoding='utf-8')
    print('equipment bridge: 16 weapon templates, 10 profiles, 3 armor templates')


if __name__ == '__main__':
    main()
