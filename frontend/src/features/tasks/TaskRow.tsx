import { useState } from 'react'
import { formatDue } from './dateTime'
import { TaskForm } from './TaskForm'
import type { Task } from '../../shared/db'
import type { TaskInput } from './taskStore'

interface TaskRowProps {
  task: Task
  onToggle: (task: Task) => Promise<void>
  onUpdate: (task: Task, input: TaskInput) => Promise<void>
  onDelete: (task: Task) => Promise<void>
}

export function TaskRow({ task, onToggle, onUpdate, onDelete }: TaskRowProps) {
  const [editing, setEditing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  if (editing) {
    return (
      <TaskForm
        initial={{ title: task.title, notes: task.notes, dueAt: task.dueAt }}
        submitLabel="Save"
        onSubmit={async (input) => {
          await onUpdate(task, input)
          setEditing(false)
        }}
        onCancel={() => setEditing(false)}
      />
    )
  }

  const run = async (action: () => Promise<void>) => {
    setBusy(true)
    try {
      await action()
    } finally {
      setBusy(false)
    }
  }

  const completed = task.completedAt !== null

  return (
    <article className={completed ? 'task-row task-row--completed' : 'task-row'}>
      <input
        type="checkbox"
        aria-label={completed ? 'Reopen task' : 'Complete task'}
        checked={completed}
        disabled={busy}
        onChange={() => void run(() => onToggle(task))}
      />
      <div className="task-row__body">
        <strong className="task-row__title">{task.title}</strong>
        {task.notes && <small>{task.notes}</small>}
        <small>Due: {formatDue(task.dueAt)}</small>
      </div>
      {confirmingDelete ? (
        <div className="task-row__actions">
          <span>Delete?</span>
          <button type="button" disabled={busy} onClick={() => void run(() => onDelete(task)).then(() => setConfirmingDelete(false))}>
            Yes
          </button>
          <button type="button" className="secondary outline" disabled={busy} onClick={() => setConfirmingDelete(false)}>
            No
          </button>
        </div>
      ) : (
        <div className="task-row__actions">
          <button type="button" className="secondary outline" disabled={busy} onClick={() => setEditing(true)}>
            Edit
          </button>
          <button type="button" className="secondary outline" disabled={busy} onClick={() => setConfirmingDelete(true)}>
            Delete
          </button>
        </div>
      )}
    </article>
  )
}
