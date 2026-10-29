"""Build a source snapshot and method-level index for the local combat audit.

This records review depth explicitly. Exported native instructions and protected
item values remain local evidence, never inputs to the game's runtime/build.
"""
import argparse
import collections
import hashlib
import json
import math
import struct
from datetime import datetime, timezone
from pathlib import Path


COUNTERPARTS = {
    'motion': ['DuelReference.cs', 'DuelReferenceKick.cs', 'DuelFighter.cs'],
    'input': ['DuelControls.cs', 'DuelReference.cs'],
    'injury': ['DuelReference.cs', 'DuelReferenceKick.cs'],
    'equipment': ['DuelReference.cs', 'DuelWeapons.cs', 'DuelFigure.cs'],
    'ai': ['DuelAI.cs'],
    'rounds': ['PhysicsDuel.cs', 'DuelCampaign.cs'],
    'network': ['DuelCampaign.cs'],
    'feedback': ['DuelEffects.cs', 'DuelFigure.cs', 'PhysicsDuel.cs'],
    'special': [],
}

# Hand-checked method bodies, not inferred from automatic instruction counts.
# Review of an individual method does not certify its complete call graph.
CONTROL_FLOW = {
    'CharacterMover': 'FixedUpdate clm cln clq clr clt clu SetHeadForceUpwardsBasedOnHealth SetHandCOMMassAfterTwoHandedDroppedFromTheOtherHand StartKick',
    'CharacterDamageCalculator': 'RegisterImpact OnHandItemImpact_Authoritative cgg OnHandItemOverlapInternalOrgan',
    'BodyPart': 'ApplyDamage ProcessImpact cdv cdt cdu cdw SetPhysicsProps_PassedOut SetPhysicsProps_Dead',
    'CharacterVitality': 'FixedUpdateNetwork ApplyDamage ConsumeStamina cms cmt cmu cmv',
    'StaminaDrainer': 'Initialize StartDraining DrainTick StopDraining',
    'KickReceiver': 'OnEnable OnTriggerEnter2D',
    'KickReceiver/ct': 'MoveNext',
    'ImpactInfo': '.ctor op_Implicit',
    'InternalOrgan': 'OnTriggerEnter2D',
    'SourcePrefab_HandItem': 'cqe SetDropped',
    'MatchRules': '.ctor',
    'MatchRulesSurvival': 'GetStartingHealth',
}
CALL_SUMMARY = {
    'CharacterMover': 'cll cls clx cly clz cma FixedUpdateNetwork',
    'BodyPart': 'OnCollisionEnter2D cdx cdy DropHandItem RebakeJointAtRest SetPhysicsProps_Normal',
    'CharacterVitality': 'Spawned StartBleeding SetUpWeaponForStaminaDrain SetUpShieldForStaminaDrain',
    'CharacterDamageCalculator': 'BuildAndPublishPresentation',
    'CharacterDamageCalculator/bx': 'MoveNext',
    'CharacterEnemyAI': 'Update FixedUpdate SetAIWeaponAndShieldProperties',
    'AIHandsInputRelayer': 'SetAIHandAnimBundle Attack StartDefendingCoroutine',
    'CharacterConfigApplier': 'ApplyHeight ApplyWeight ApplyHandItemLeft ApplyDoubleHandedItem ApplyArmorItem',
    'SourcePrefab_HandItem': 'Start cqf cqg FixedUpdate OnCollisionEnter2D cqh EndImpalement',
    'PlayerInputHandler': 'GetNetworkInput FixedUpdateNetwork',
    'MatchRules': 'crv crw crx ComputeStars',
    'MatchRulesFriendly': 'OnPlayerDeath',
    'MatchRulesPractice': 'OnPlayerDeath',
    'MatchRulesRandom': 'OnPlayerDeath',
    'MatchRulesRanked': 'OnPlayerDeath',
    'MatchRulesSurvival': 'OnPlayerDeath',
    'Cannonball': 'OnCollisionEnter2D',
    'DestroyIfFallen': 'Start',
    'MatchEndHandler': 'crr crs crt cru',
}


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def decoded_float(data):
    """Native ACTkByte4.UnShuffle then XOR, verified for this APK version.

    Plain XOR of hiddenValue and key is wrong: bytes 1/2 are interchanged.
    No APK is executed, patched or re-signed by this inspection.
    """
    bits = bytearray(struct.pack('<I', data['hiddenValue'] & 0xffffffff))
    bits[1], bits[2] = bits[2], bits[1]
    clear = int.from_bytes(bits, 'little') ^ (data['currentCryptoKey'] & 0xffffffff)
    return struct.unpack('<f', struct.pack('<I', clear))[0]


def main(args):
    repo = args.repo.resolve()
    ledger = json.loads(args.ledger.read_text(encoding='utf-8-sig'))
    combat = repo / 'unity/JianghuUnity/Assets/Scripts/PhysicsCombat'
    rows = []
    for method in ledger['methods']:
        t, n = method['type'], method['name']
        if n in CONTROL_FLOW.get(t, '').split():
            depth = 'control-flow-reviewed'
        elif n in CALL_SUMMARY.get(t, '').split():
            depth = 'call-and-field-summary-reviewed'
        else:
            depth = 'inventory-only'
        candidates = [str((combat / f).relative_to(repo)).replace('\\', '/')
                      for f in COUNTERPARTS[method['group']] if (combat / f).exists()]
        rows.append(dict(type=t, name=n, signature=method['signature'], group=method['group'],
                         file_offset=method.get('file_offset'), va=method.get('va'),
                         instruction_count=method['instruction_count'], review_depth=depth,
                         counterpart_candidates=candidates,
                         counterpart_status='no-current-firearm-implementation' if method['group']=='special'
                         else 'different-single-player-save-architecture' if method['group']=='network'
                         else 'candidate-only-not-certified-equivalent'))
    evidence = json.loads(args.serialized.read_text(encoding='utf-8-sig'))
    item_values, bodies = [], []
    for record in evidence['records']:
        data = record['data']
        if record.get('script') == 'SourcePrefab_HandItem':
            values = {key: decoded_float(data[key]) for key in ('obs_Damage', 'obs_Defense', 'obs_Weight')}
            if not all(math.isfinite(v) for v in values.values()):
                raise ValueError('Nonfinite decoded item value')
            item_values.append(dict(file=record['file'], path_id=record['id'], owner=record['owner'],
                                    item_id=data.get('itemId'), name=data['Name'], category=data['HandItemType'],
                                    **values))
        if record.get('script') == 'BodyPart' and record['file'] == '00af04d95140a4b44ac8453d91c032ea':
            keys = ['MaxHealth', 'overallDamageMultiplier', 'passedOutMass', 'passedOutDrag',
                    'passedOutAngularDrag', 'deadMass', 'deadDrag', 'deadAngularDrag']
            bodies.append(dict(owner=record['owner'], values={k:data[k] for k in keys}))
    args.local_values.parent.mkdir(parents=True, exist_ok=True)
    args.local_values.write_text(json.dumps(dict(items=item_values, bodies=bodies), ensure_ascii=False, indent=2), encoding='utf-8')
    source_paths = [*sorted(combat.glob('*.cs')),
                    repo/'jianghu/engine.py',
                    repo/'jianghu/physical_combat.py', repo/'jianghu/physical_reference.py',
                    repo/'jianghu/combat_attributes.py', repo/'jianghu/weapon_catalog.json',
                    repo/'jianghu/weapons.py',repo/'unity/JianghuUnity/Assets/Scripts/PhysicalBattleScreen.cs']
    snapshot = {str(p.relative_to(repo)).replace('\\','/'):digest(p) for p in source_paths}
    method_output = args.output.resolve().with_name('bloody-code-methods-20261028.json')
    method_output.write_text(json.dumps(dict(schema=1, methods=rows), ensure_ascii=False, indent=2), encoding='utf-8')
    groups = collections.Counter(m['group'] for m in rows)
    depths = collections.Counter(m['review_depth'] for m in rows)
    examples = {
        'same_contact_wood_sword_damage_0_8_to_iron_sword_1': {
            'display_stat_ratio':1/.8, 'raw_reference_formula_ratio':(1+1/15)/(1+.8/15)},
        'metal_handle_before_clamp': {'source_multiplier':.4, 'ours_multiplier':.2},
        'shield_stat_1_at_weighted_speed_30_unarmored': {
            'source_local_damage':30*(1+1/15)*.25*2.15*.5,
            'ours_local_damage':30*.1*.5},
        'max_stamina_140_wake': {'source':10,'ours':14},
        'head_support_max_health_140_full': {'source':(140+100)*.25,'ours':(100+100)*.25},
    }
    result = dict(schema=1, kind='static-code-audit-not-gameplay-acceptance',
                  generated_at=datetime.now(timezone.utc).isoformat(), source_root=str(repo),
                  apk_sha256=digest(args.apk), native_sha256=digest(args.native),
                  active_build_marker=(repo/'unity/Build/active-build.txt').read_text().strip(),
                  scope=dict(types=len(ledger['types']), methods=len(rows),
                             instructions=sum(m['instruction_count'] for m in rows), groups=groups,
                             review_depth=depths),
                  method_index=str(method_output.relative_to(repo)).replace('\\','/'),
                  values_local_only=str(args.local_values), source_sha256=snapshot,
                  arithmetic_examples=examples, claims_complete_semantic_review=False,
                  claims_one_to_one=False, game_code_changed_by_this_audit=False,
                  finding_ids=[f'A{i:02}' for i in range(1,21)],
                  findings_report='docs/validation/BLOODY_SOURCE_PORT_VALIDATION.md',
                  limits=['Inventory scope is explicit, not proof that all combat dependencies were found.',
                          'Inventory-only entries have not been semantically reviewed.',
                          'Candidate counterpart files do not certify method-level equivalence.',
                          'Modified APK static data is not a measurement of official live runtime.',
                          'No new Unity battle or original-game trajectory comparison was run.'])
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2),encoding='utf-8')
    print(json.dumps(dict(scope=result['scope'], evidence_items=len(item_values), body_parts=len(bodies)),ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('repo','ledger','serialized','native','apk','local-values','output'):
        parser.add_argument('--'+name, type=Path, required=True)
    main(parser.parse_args())
