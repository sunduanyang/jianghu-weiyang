import json
import sqlite3
from pathlib import Path


class Store:
    """One objective journal, indexed participant histories, atomic world checkpoints."""
    def __init__(self, path=':memory:'):
        self.path = str(path)
        if self.path != ':memory:':
            Path(path).parent.mkdir(parents=True, exist_ok=True)
        self.db = sqlite3.connect(self.path, check_same_thread=False)
        self.db.execute('PRAGMA journal_mode=WAL')
        self.db.executescript('''
            CREATE TABLE IF NOT EXISTS events(id INTEGER PRIMARY KEY, month INTEGER,
                kind TEXT, importance REAL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS participants(event_id INTEGER, npc_id INTEGER,
                PRIMARY KEY(event_id,npc_id));
            CREATE INDEX IF NOT EXISTS npc_events ON participants(npc_id,event_id DESC);
            CREATE INDEX IF NOT EXISTS important_events ON events(importance,id DESC);
            CREATE TABLE IF NOT EXISTS checkpoint(id INTEGER PRIMARY KEY CHECK(id=1),data TEXT);
            CREATE TABLE IF NOT EXISTS player_checkpoint(id INTEGER PRIMARY KEY CHECK(id=1),
                month INTEGER NOT NULL,data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ai_log(id INTEGER PRIMARY KEY, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS experiences(id INTEGER PRIMARY KEY, event_id INTEGER,
                npc_id INTEGER, data TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS person_experience ON experiences(npc_id,event_id,id);
        ''')
        self.next_id = self.db.execute('SELECT coalesce(max(id),0)+1 FROM events').fetchone()[0]

    def append(self, event):
        event['id'] = self.next_id
        self.next_id += 1
        self.db.execute('INSERT INTO events VALUES(?,?,?,?,?)',
                        (event['id'], event['month'], event['kind'], event['importance'],
                         json.dumps(event, ensure_ascii=False, separators=(',', ':'))))
        self.db.executemany('INSERT INTO participants VALUES(?,?)',
                            [(event['id'], i) for i in set(event['people'])])
        return event

    def event(self, event_id):
        row = self.db.execute('SELECT data FROM events WHERE id=?', (event_id,)).fetchone()
        return json.loads(row[0]) if row else None

    def history(self, npc_id=None, before=None, limit=60, important=False):
        conditions, args = [], []
        if npc_id is not None:
            conditions.append('e.id IN (SELECT event_id FROM participants WHERE npc_id=?)')
            args.append(npc_id)
        if before:
            conditions.append('e.id<?')
            args.append(before)
        if important:
            conditions.append('e.importance>=0.7')
        sql = 'SELECT e.data FROM events e'
        if conditions:
            sql += ' WHERE ' + ' AND '.join(conditions)
        rows = self.db.execute(sql + ' ORDER BY e.id DESC LIMIT ?', (*args, limit)).fetchall()
        events = [json.loads(r[0]) for r in rows]
        if npc_id is not None:
            for e in events:
                row = self.db.execute('SELECT data FROM experiences WHERE npc_id=? AND event_id=? ORDER BY id DESC LIMIT 1',
                                      (npc_id, e['id'])).fetchone()
                if row:
                    e['perspective'] = json.loads(row[0])
        return events

    def experience(self, npc_id, memory, recorded_month):
        # Append reinterpretations as new revisions. Forgetting a working memory never
        # removes the historical evidence of what this person once thought or changed.
        record = {**memory, 'recorded_month': recorded_month, 'npc_id': npc_id}
        self.db.execute('INSERT INTO experiences(event_id,npc_id,data) VALUES(?,?,?)',
                        (memory['event_id'], npc_id, json.dumps(record, ensure_ascii=False)))

    def personal_memories(self, npc_id, keywords, limit=60):
        if not keywords:
            return []
        terms = list(dict.fromkeys(keywords))[:16]
        clauses = ' OR '.join("data LIKE ? ESCAPE '\\'" for _ in terms)
        patterns = ['%' + key.replace('\\', '\\\\').replace('%', '\\%').replace('_', '\\_') + '%' for key in terms]
        rows = self.db.execute('SELECT data FROM experiences WHERE npc_id=? AND (' + clauses +
                               ') ORDER BY event_id DESC,id DESC LIMIT ?', (npc_id, *patterns, limit * 2)).fetchall()
        result = {}
        for row in rows:
            memory = json.loads(row[0])
            result.setdefault(memory['event_id'], memory)
            if len(result) >= limit:
                break
        return list(result.values())

    def perspectives(self, event_id):
        rows = self.db.execute('''SELECT data FROM experiences WHERE id IN
            (SELECT max(id) FROM experiences WHERE event_id=? GROUP BY npc_id)''', (event_id,)).fetchall()
        return [json.loads(row[0]) for row in rows]

    def save(self, state):
        self.db.execute('INSERT OR REPLACE INTO checkpoint VALUES(1,?)',
                        (json.dumps(state, ensure_ascii=False, separators=(',', ':')),))
        self.db.execute('DELETE FROM player_checkpoint')
        self.db.commit()

    def save_player(self, player, month):
        # Walking changes only the player. Commit its journal entries and position
        # together without serializing every NPC on every adjacent step.
        self.db.execute('INSERT OR REPLACE INTO player_checkpoint VALUES(1,?,?)',
                        (month, json.dumps(player, ensure_ascii=False, separators=(',', ':'))))
        self.db.commit()

    def load(self):
        self.next_id = self.db.execute('SELECT coalesce(max(id),0)+1 FROM events').fetchone()[0]
        row = self.db.execute('SELECT data FROM checkpoint WHERE id=1').fetchone()
        if not row:
            return None
        state = json.loads(row[0])
        saved_player = self.db.execute('SELECT month,data FROM player_checkpoint WHERE id=1').fetchone()
        if saved_player and saved_player[0] == state['month']:
            player = json.loads(saved_player[1])
            if player['protagonist']['revision'] >= state['player'].get('protagonist', {}).get('revision', -1):
                state['player'] = player
        return state

    def log_ai(self, record):
        self.db.execute('INSERT INTO ai_log(data) VALUES(?)',
                        (json.dumps(record, ensure_ascii=False),))

    def count(self):
        return self.next_id - 1

    def close(self):
        self.db.close()
