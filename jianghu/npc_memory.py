"""Goal-relevant retrieval from one person's own attributed experiences."""
from copy import deepcopy

TOPICS = {
    'wealth': ('营生', '学艺', '手艺', '转业', '合营', '托银', '侵吞'),
    'mastery': ('师', '传艺', '请益', '习武', '武学', '贡献'),
    'renown': ('差事', '贡献', '公帑', '传艺', '结义'),
    'belonging': ('亲', '照料', '接济', '婚', '结义', '赔礼'),
    'revenge': ('窃取', '勒索', '报复', '寻仇', '背叛', '中伤', '仇怨'),
    'freedom': ('远行', '抵达', '行旅', '迁', '驿'),
}


def contacts(n, limit=3):
    p = n.get('plan') or {}
    priority = {i for i in (p.get('tutor'), p.get('target_person'), n['mentor'], n['spouse'], n['guardian']) if i}
    return sorted(n['relationships'].items(),
                  key=lambda row: (-(int(row[0]) in priority), -abs(row[1]['affinity']), int(row[0])))[:limit]


def topics(n):
    keys = list(TOPICS.get((n.get('goal') or {}).get('kind'), ()))
    p = n.get('plan') or {}
    if p.get('intent') == 'career_change':
        keys = ['学艺', '转业', p['profession']] + keys
    elif p.get('intent') == 'sect_training':
        keys = ['贡献', '请益', '传艺'] + keys
    names = [n['knowledge'].get(key, {}).get('name') for key, _ in contacts(n)]
    return list(dict.fromkeys(keys)), [name for name in names if name]


def retrieve(w, n, limit=3):
    keys, names = topics(n)
    # Query only this NPC's experience rows; never the objective event journal.
    archived = w.store.personal_memories(n['id'], names + keys, limit=60)
    pool = {m['event_id']: m for m in archived}
    pool.update({m['event_id']: m for m in n['memories']})
    def score(m):
        text = m['fact'] + ' ' + m['interpretation']
        relevance = min(3, sum(key in text for key in keys))
        persons = min(2, sum(name in text for name in names))
        age = max(0, w.month - m['month'])
        return relevance * 2.5 + persons * 3. + m['salience'] * 2 + 1 / (1 + age / 12)
    return [deepcopy(m) for m in sorted(pool.values(), key=lambda m: (-score(m), -m['event_id']))[:limit]]


def find(w, n, event_id):
    current = next((m for m in n['memories'] if m['event_id'] == event_id), None)
    if current is not None:
        return deepcopy(current)
    rows = w.store.db.execute('SELECT data FROM experiences WHERE npc_id=? AND event_id=? ORDER BY id DESC LIMIT 1',
                              (n['id'], event_id)).fetchone()
    if rows:
        import json
        return json.loads(rows[0])
    return None
