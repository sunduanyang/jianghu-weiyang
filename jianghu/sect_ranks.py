from . import combat_attributes
"""One nine-grade ladder shared by every sect, NPC and protagonist."""

VERSION = 1
GRADES = {i: numeral+'品' for i, numeral in enumerate('一二三四五六七八九', 1)}
# Grade one follows the actual presiding office. Other grades need both gates.
# Retain the existing steward (12 / 25) and elder (35 / 45) milestones.
REQUIREMENTS = {
    1: None,
    2: (70, 65),
    3: (35, 45),
    4: (24, 35),
    5: (12, 25),
    6: (8, 20),
    7: (4, 16),
    8: (1, 12),
    9: (0, 0),
}


def merit_grade(actor):
    progress = actor.get('rank_progress', {}).get('sect') or {}
    merit = max(0., actor['contribution'] - progress.get('penalty', 0.))
    return next(g for g, requirement in REQUIREMENTS.items() if requirement is not None
                and merit >= requirement[0] and combat_attributes.rating(actor) >= requirement[1])


def standing(faction, actor):
    if actor['id'] == faction['leader']:
        grade = 1
    else:
        progress = actor.get('rank_progress', {}).get('sect') or {}
        grade = progress['grade'] if progress.get('faction') == faction['id'] else merit_grade(actor)
    return dict(grade=grade, grade_label=GRADES[grade], title=faction['ranks'][grade-1])


def ladder(faction):
    result = []
    for grade, requirement in REQUIREMENTS.items():
        result.append(dict(grade=grade, grade_label=GRADES[grade], title=faction['ranks'][grade-1],
                           min_contribution=requirement[0] if requirement else None,
                           min_skill=requirement[1] if requirement else None,
                           leader_only=grade == 1,
                           condition=('现任主持' if grade == 1 else '入门登记' if grade == 9 else
                                      f'贡献{requirement[0]} · 武学{requirement[1]}')))
    return result
