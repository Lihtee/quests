import { useState, type FormEvent } from 'react'
import { validateTask } from '../../domain/taskRules'
import { fromLocalInputValue, toLocalInputValue } from './dateTime'
import { TaskValidationError, type TaskInput } from './taskStore'

interface TaskFormProps {
  initial?: TaskInput
  submitLabel: string
  onSubmit: (input: TaskInput) => Promise<void>
  onCancel?: () => void
}

const empty: TaskInput = { title: '', notes: null, dueAt: null }

/** Create/edit form. Resets after a successful submit when there is no Cancel (create mode). */
export function TaskForm({ initial = empty, submitLabel, onSubmit, onCancel }: TaskFormProps) {
  const [title, setTitle] = useState(initial.title)
  const [notes, setNotes] = useState(initial.notes ?? '')
  const [due, setDue] = useState(toLocalInputValue(initial.dueAt))
  const [saving, setSaving] = useState(false)
  const [errors, setErrors] = useState<string[]>([])

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    const input: TaskInput = { title, notes: notes || null, dueAt: fromLocalInputValue(due) }
    const invalid = Object.values(validateTask(input))
    setErrors(invalid)
    if (invalid.length > 0) return

    setSaving(true)
    try {
      await onSubmit(input)
      if (!onCancel) {
        setTitle('')
        setNotes('')
        setDue('')
      }
    } catch (error) {
      if (import.meta.env.DEV) console.error('[tasks] save failed', error)
      setErrors(errorMessages(error))
    } finally {
      setSaving(false)
    }
  }

  return (
    <form onSubmit={(event) => void handleSubmit(event)}>
      <input
        type="text"
        placeholder="Title"
        aria-label="Title"
        value={title}
        onChange={(event) => setTitle(event.target.value)}
        required
      />
      <textarea
        placeholder="Notes (optional)"
        aria-label="Notes"
        rows={2}
        value={notes}
        onChange={(event) => setNotes(event.target.value)}
      />
      <label>
        Due (optional)
        <input type="datetime-local" value={due} onChange={(event) => setDue(event.target.value)} />
      </label>
      {errors.length > 0 && (
        <ul role="alert">
          {errors.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      )}
      <div role="group">
        <button type="submit" disabled={saving} aria-busy={saving}>
          {submitLabel}
        </button>
        {onCancel && (
          <button type="button" className="secondary" onClick={onCancel} disabled={saving}>
            Cancel
          </button>
        )}
      </div>
    </form>
  )
}

function errorMessages(error: unknown): string[] {
  if (error instanceof TaskValidationError) return Object.values(error.errors)
  return [error instanceof Error ? error.message : 'Could not save the task']
}
