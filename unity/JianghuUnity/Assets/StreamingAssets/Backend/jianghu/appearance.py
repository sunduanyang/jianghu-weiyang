"""Persistent appearance, independent of the simulation's random stream.

Only real births may inherit features. Legacy migration assigns opening looks;
it does not infer ancestry, past injuries, clothing purchases or other events.
"""
import hashlib
import random

VERSION = 2
MALE_HEADS = ('m-youth-farmer', 'm-middle-farmer', 'm-scholar', 'm-merchant',
              'm-escort', 'm-hunter', 'm-artisan', 'm-performer')
FEMALE_HEADS = ('f-youth-farmer', 'f-middle-farmer', 'f-scholar', 'f-merchant',
                'f-escort', 'f-swordswoman')
PALETTES = ('黛青', '赭石', '松绿', '灰紫', '靛蓝', '枣红', '茶褐', '月白')
SKIN = ('白皙', '温润', '麦色', '深麦色')
HAIR = ('乌黑', '墨褐', '深棕', '灰黑')
EYES = ('丹凤', '冷眸', '含笑')
BROWS = ('清秀', '英气', '舒展')
LIPS = ('朱唇', '微扬', '淡绛')
MODULES = ('eyes', 'brows', 'lips', 'outfit', 'head_variant')


def prepare(w, actor, *, player=False, inherit=False):
    """Fill once, preserve existing profiles and never consume w.rng."""
    existing = actor.get('appearance')
    if (isinstance(existing, dict) and existing.get('version') == VERSION
            and all(k in existing for k in MODULES)):
        return existing
    # Keep the original identity stream across the renderer migration.
    key = f"appearance-v1:{w.seed}:{'player' if player else actor['id']}"
    seed = hashlib.sha256(key.encode('utf-8')).hexdigest()[:16]
    rng = random.Random(int(seed, 16))
    heads = FEMALE_HEADS if actor.get('sex') == '女' else MALE_HEADS
    migrating = isinstance(existing, dict) and existing.get('version') in (1, VERSION)
    profile = dict(existing) if migrating else dict(
        version=VERSION, seed=seed, head=rng.choice(heads),
        skin=rng.randrange(len(SKIN)), hair=rng.randrange(len(HAIR)),
        palette=rng.randrange(len(PALETTES)), face_width=rng.randrange(5),
        source='出生生成' if inherit else '期初外貌登记')
    if inherit and not migrating:
        parents = [w.npcs[i] for i in actor.get('parents', ()) if i in w.npcs]
        looks = [prepare(w, p) for p in parents]
        if looks:
            profile['skin'] = rng.choice(looks)['skin']
            profile['hair'] = rng.choice(looks)['hair']
            same_sex = [p['appearance'] for p in parents if p['sex'] == actor['sex']]
            if same_sex and rng.random() < .65:
                profile['head'] = rng.choice(same_sex)['head']
            profile['face_width'] = rng.choice(looks)['face_width']
    modular_seed = hashlib.sha256((profile['seed'] + ':modules-v2').encode()).hexdigest()
    modules = random.Random(int(modular_seed[:16], 16))
    for field, limit in (('eyes', 3), ('brows', 3), ('lips', 3),
                         ('outfit', 2), ('head_variant', 2)):
        profile.setdefault(field, modules.randrange(limit))
    if inherit and not migrating:
        if looks:
            for field in ('eyes', 'brows', 'lips', 'head_variant'):
                if modules.random() < .65:
                    profile[field] = modules.choice(looks)[field]
    profile['version'] = VERSION
    actor['appearance'] = profile
    return profile


def view(actor):
    p = actor.get('appearance')
    if not p:
        return None
    years = actor.get('age_months', 216) / 12
    # Adult facial identity survives birthdays; gradual silvering has six steps.
    grey = max(0, min(5, int((years - 42) / 7)))
    return dict(head=p['head'], grey=grey, palette=p['palette'],
                eyes=p.get('eyes', 0), brows=p.get('brows', 0), lips=p.get('lips', 0),
                description=f"{SKIN[p['skin']]}肤色，{HAIR[p['hair']] if not grey else '鬓发渐白' if grey < 4 else '银发'}，"
                            f"{BROWS[p.get('brows', 0)]}眉、{EYES[p.get('eyes', 0)]}眼，常着{PALETTES[p['palette']]}衣衫")
