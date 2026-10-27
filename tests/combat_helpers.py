"""Fixtures for independent abilities; no legacy martial proficiency is stored."""
from jianghu import combat_attributes as abilities


def set_rating(actor, value):
    abilities.initialize(actor)
    actor['combat_attributes']['base'].update(dict.fromkeys(abilities.KEYS, value))


def update_actor(actor, *mappings, **values):
    merged={}
    for mapping in mappings:merged.update(mapping)
    merged.update(values)
    if 'skill' in merged:set_rating(actor, merged.pop('skill'))
    actor.update(merged)
