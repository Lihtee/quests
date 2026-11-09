import { useEffect, useState } from 'react'
import { TaskForm } from './TaskForm'
import { TaskRow } from './TaskRow'
import { completeTask, createTask, deleteTask, listTasks, reopenTask, updateTask, type Task, type TaskInput } from './tasksApi'

type ListState = { kind: 'loading' } | { kind: 'ok'; tasks: Task[] } | { kind: 'error'; message: string }

const messageOf = (error: unknown) => (error instanceof Error ? error.message : 'Unexpected error')

async function loadList(): Promise<ListState> {
  try {
    return { kind: 'ok', tasks: await listTasks() }
  } catch (error) {
    if (import.meta.env.DEV) console.error('[tasks] list failed', error)
    return { kind: 'error', message: messageOf(error) }
  }
}

/** Online-only task list. After each change the list is refetched, so the order always matches the server. */
export function TasksPage() {
  const [state, setState] = useState<ListState>({ kind: 'loading' })
  const [showCompleted, setShowCompleted] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  const refresh = async () => setState(await loadList())

  useEffect(() => {
    let active = true
    void loadList().then((next) => {
      if (active) setState(next)
    })
    return () => {
      active = false
    }
  }, [])

  const retry = () => {
    setState({ kind: 'loading' })
    void refresh()
  }

  /** Runs a row action (toggle/delete); failures are shown above the list instead of thrown. */
  const runAction = async (name: string, action: () => Promise<unknown>) => {
    setActionError(null)
    try {
      await action()
    } catch (error) {
      if (import.meta.env.DEV) console.error(`[tasks] ${name} failed`, error)
      setActionError(`Could not ${name} the task: ${messageOf(error)}`)
    }
    await refresh()
  }

  // Create/update errors are thrown so TaskForm can show validation messages.
  const handleCreate = async (input: TaskInput) => {
    await createTask(input)
    await refresh()
  }

  const handleUpdate = async (task: Task, input: TaskInput) => {
    await updateTask(task.id, input)
    await refresh()
  }

  const handleToggle = (task: Task) =>
    task.completedAt === null
      ? runAction('complete', () => completeTask(task.id))
      : runAction('reopen', () => reopenTask(task.id))

  const handleDelete = (task: Task) => runAction('delete', () => deleteTask(task.id))

  if (state.kind === 'loading') return <p aria-busy="true">Loading tasks…</p>

  if (state.kind === 'error') {
    return (
      <>
        <p>Could not load tasks: {state.message}</p>
        <button type="button" onClick={retry}>
          Retry
        </button>
      </>
    )
  }

  const visible = showCompleted ? state.tasks : state.tasks.filter((t) => t.completedAt === null)

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
