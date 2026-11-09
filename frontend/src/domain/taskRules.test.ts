import { describe, expect, it } from 'vitest'
import { NOTES_MAX_LENGTH, TITLE_MAX_LENGTH, normalizeTask, sortTasks, validateTask } from './taskRules'

const task = (id: string, dueAt: string | null, createdAt: string, completedAt: string | null = null) => ({
  id,
  dueAt,
  createdAt,
  completedAt,
})

describe('sortTasks', () => {
  it('puts dated open tasks first (earlier due first), then undated, then completed newest first', () => {
    const tasks = [
      task('undated-late', null, '2026-11-09T10:00:00Z'),
      task('done-old', '2026-11-01T00:00:00Z', '2026-11-01T00:00:00Z', '2026-11-02T00:00:00Z'),
      task('later', '2026-11-12T00:00:00Z', '2026-11-09T00:00:00Z'),
      task('undated-early', null, '2026-11-09T09:00:00Z'),
      task('sooner', '2026-11-10T03:00:00+03:00', '2026-11-09T00:00:00Z'),
      task('done-new', null, '2026-11-01T00:00:00Z', '2026-11-08T00:00:00Z'),
      task('sooner-created-later', '2026-11-10T00:00:00.000Z', '2026-11-09T05:00:00Z'),
    ]

    expect(sortTasks(tasks).map((t) => t.id)).toEqual([
      'sooner',
      'sooner-created-later',
      'later',
      'undated-early',
      'undated-late',
      'done-new',
      'done-old',
    ])
  })
})

describe('validateTask', () => {
  it('requires a title after trimming and limits lengths', () => {
    expect(validateTask({ title: 'ok', notes: null })).toEqual({})
    expect(validateTask({ title: '   ', notes: null }).title).toBeDefined()
    expect(validateTask({ title: ` ${'a'.repeat(TITLE_MAX_LENGTH)} `, notes: null })).toEqual({})
    expect(validateTask({ title: 'a'.repeat(TITLE_MAX_LENGTH + 1), notes: null }).title).toBeDefined()
    expect(validateTask({ title: 'ok', notes: 'n'.repeat(NOTES_MAX_LENGTH) })).toEqual({})
    expect(validateTask({ title: 'ok', notes: 'n'.repeat(NOTES_MAX_LENGTH + 1) }).notes).toBeDefined()
  })
})

describe('normalizeTask', () => {
  it('trims the title and turns blank notes into null', () => {
    expect(normalizeTask({ title: '  Buy milk ', notes: '   ' })).toEqual({ title: 'Buy milk', notes: null })
    expect(normalizeTask({ title: 'x', notes: ' keep ' })).toEqual({ title: 'x', notes: ' keep ' })
  })
})
