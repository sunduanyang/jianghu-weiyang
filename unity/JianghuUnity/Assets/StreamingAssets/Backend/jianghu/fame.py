"""名声远近：a reputation is known where people have seen it or heard of it.

`reputation` stays one number — what a person has done. How widely it is known
is kept per town as a share from 0 to 1: living somewhere makes a person fully
known there within months; hearsay carries a name to neighbouring towns, and
the greater the renown or infamy the better it is known there; places a person left slowly forget them. Anything
judged by local people (local office, notable status, how wary a would-be
offender is of their own infamy) reads the locally known reputation. Town keys are
stored as strings so saves reload identically. Spreading is deterministic and never consumes the world RNG.
"""

VERSION = 1
PRESENT = .25     # monthly closing of the gap where one lives
FORGET = .97      # monthly retention elsewhere
FLOOR = .02       # shares below this are forgotten entirely
NOTICED = 5.      # reputations smaller than this do not travel
REACH_CAP = .8    # neighbours never know a name as well as its home town does
HEARSAY = .1      # monthly closing of the gap in a neighbouring town


def shares(holder):
    """The per-town shares of a person or the player (legacy: fully known where they are). Pure."""
    m = holder.get('fame')
    if m is None:
        place = holder['place'] if 'place' in holder else (holder.get('protagonist') or {}).get('place')
        m = {str(place): 1.} if place is not None else {}
    return m


def known(holder, place):
    return shares(holder).get(str(place), 0.)


def local(holder, place=None):
    """The reputation people in `place` (default: where the holder is) actually know of."""
    if place is None:
        place = holder['place'] if 'place' in holder else (holder.get('protagonist') or {}).get('place')
    return round(holder['reputation'] * known(holder, place), 4)


def spread(w, holder, place):
    m = shares(holder)
    place = str(place)
    rep = abs(holder['reputation'])
    # Neighbouring towns come to know a name only as far as it is worth talking about.
    reach = min(REACH_CAP, rep / 100) * m.get(place, 0.)
    here = m.get(place, 0.) + (1 - m.get(place, 0.)) * PRESENT
    nxt = {}
    for town, share in m.items():
        if town != place:
            share *= FORGET
            if share >= FLOOR:
                nxt[town] = round(share, 4)
    if rep >= NOTICED:
        for town in map(str, w.neighbors.get(int(place), [])):
            heard = nxt.get(town, 0.)
            if reach > heard:
                nxt[town] = round(heard + (reach - heard) * HEARSAY, 4)
    nxt[place] = round(here, 4)
    holder['fame'] = nxt


def monthly(w):
    for n in w.npcs.values():
        if n['alive'] and n['age_months'] >= 192:
            spread(w, n, n['place'])
    h = w.player.get('protagonist') or {}
    if h.get('created') and h.get('place') is not None:
        spread(w, w.player, h['place'])


def view(w, holder):
    from .atlas import PLACES
    rows = sorted(((s, int(t)) for t, s in shares(holder).items()), reverse=True)
    return dict(version=VERSION, reputation=holder['reputation'],
                towns=[dict(place=t, name=PLACES[t]['name'], known=round(s, 3),
                            local=round(holder['reputation']*s, 2)) for s, t in rows[:8]],
                description='声望是所作所为；名声所及按城镇记录，久居之地人尽皆知，邻近城镇靠传闻渐知，离开后渐被淡忘。')


def local_of(w, who):
    """Locally known reputation of the player ('player') or an NPC id, where they are now."""
    if who == 'player' or who == -1:
        return local(w.player, w.player['protagonist'].get('place'))
    n = w.npcs[int(who)]
    return local(n, n['place'])
