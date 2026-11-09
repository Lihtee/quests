import { liveQuery } from 'dexie'
import { useEffect, useState } from 'react'
import { sortTasks } from '../../domain/taskRules'
import type { QuestsDb, Task } from '../../shared/db'
import { TaskForm } from './TaskForm'
import { TaskRow } from './TaskRow'
import { completeTask, createTask, deleteTask, reopenTask, updateTask, type TaskInput } from './taskStore'

const messageOf = (error: unknown) => (error instanceof Error ? error.message : 'Unexpected error')

/** Task list read live from the local database; writes go to Dexie + outbox and sync in the background. */
export function TasksPage({ db }: { db: QuestsDb }) {
  const [tasks, setTasks] = useState<Task[] | null>(null)
  const [showCompleted, setShowCompleted] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  useEffect(() => {
    const subscription = liveQuery(() => db.tasks.filter((t) => t.deletedAt === null).toArray()).subscribe({
      next: (rows) => setTasks(sortTasks(rows)),
      error: (error: unknown) => {
        if (import.meta.env.DEV) console.error('[tasks] local read failed', error)
        setActionError(`Could not read tasks: ${messageOf(error)}`)
      },
    })
    return () => subscription.unsubscribe()
  }, [db])

  /** Runs a row action (toggle/delete); failures are shown above the list instead of thrown. */
  const runAction = async (name: string, action: () => Promise<unknown>) => {
    setActionError(null)
    try {
      await action()
    } catch (error) {
      if (import.meta.env.DEV) console.error(`[tasks] ${name} failed`, error)
      setActionError(`Could not ${name} the task: ${messageOf(error)}`)
    }
  }

  // Create/update errors are thrown so TaskForm can show them.
  const handleCreate = async (input: TaskInput) => {
    await createTask(db, input)
  }

  const handleUpdate = (task: Task, input: TaskInput) => updateTask(db, task.id, input)

  const handleToggle = (task: Task) =>
    task.completedAt === null
      ? runAction('complete', () => completeTask(db, task.id))
      : runAction('reopen', () => reopenTask(db, task.id))

  const handleDelete = (task: Task) => runAction('delete', () => deleteTask(db, task.id))

  if (tasks === null) return actionError ? <p role="alert">{actionError}</p> : <p aria-busy="true">Loading tasks…</p>

  const visible = showCompleted ? tasks : tasks.filter((t) => t.completedAt === null)

  return (
    <section>
      <TaskForm submitLabel="Add task" onSubmit={handleCreate} />
      <label>
        <input
          type="checkbox"
          role="switch"
          checked={showCompleted}
          onChange={(event) => setShowCompleted(event.target.checked)}
        />
        Show completed
      </label>
      {actionError && <p role="alert">{actionError}</p>}
      {visible.length === 0 ? (
        <p>No tasks yet</p>
      ) : (
        <ul className="task-list">
          {visible.map((task) => (
            <li key={task.id}>
              <TaskRow task={task} onToggle={handleToggle} onUpdate={handleUpdate} onDelete={handleDelete} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
