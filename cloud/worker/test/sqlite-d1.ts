import { DatabaseSync, SQLInputValue } from 'node:sqlite';
import { readFileSync, readdirSync } from 'node:fs';
import { URL } from 'node:url';

// Executes the production SQL, including pagination, JSON expressions and atomic
// batches. Deliberately avoids the previous mock's query substring shortcuts.
export function createSqliteD1() {
  const sqlite = new DatabaseSync(':memory:');
  const directory = new URL('../migrations/', import.meta.url);
  for (const file of readdirSync(directory).filter(f => f.endsWith('.sql')).sort())
    sqlite.exec(readFileSync(new URL(file, directory), 'utf8'));
  function prepare(query: string) {
    let args: SQLInputValue[] = [];
    const statement = {
      bind(...values: SQLInputValue[]) { args = values; return statement; },
      async first() { return sqlite.prepare(query).get(...args) ?? null; },
      async all() { return { results: sqlite.prepare(query).all(...args), success: true }; },
      async run() { const result = sqlite.prepare(query).run(...args); return { success: true, meta: { changes: result.changes, last_row_id: Number(result.lastInsertRowid) } }; }
    };
    return statement;
  }
  const db = { prepare, async batch(statements: any[]) {
    sqlite.exec('BEGIN');
    try { const results = []; for (const statement of statements) results.push(await statement.run()); sqlite.exec('COMMIT'); return results; }
    catch (error) { sqlite.exec('ROLLBACK'); throw error; }
  } } as unknown as D1Database;
  return { db, sqlite };
}
