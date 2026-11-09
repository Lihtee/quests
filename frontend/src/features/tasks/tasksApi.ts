import { apiGet, apiSend } from '../../shared/api'

/** Matches the API's TaskResponse. Dates are ISO 8601 strings (UTC). */
export interface Task {
  id: string
  title: string
  notes: string | null
  dueAt: string | null
  completedAt: string | null
  createdAt: string
  updatedAt: string
}

/** Body for create and full update (PUT). */
export interface TaskInput {
  title: string
  notes: string | null
  dueAt: string | null
}

const basePath = '/api/tasks'

export const listTasks = () => apiGet<Task[]>(basePath)

export const createTask = (input: TaskInput) => apiSend<Task>('POST', basePath, input)

export const updateTask = (id: string, input: TaskInput) => apiSend<Task>('PUT', `${basePath}/${id}`, input)

export const completeTask = (id: string) => apiSend<Task>('POST', `${basePath}/${id}/complete`)

export const reopenTask = (id: string) => apiSend<Task>('POST', `${basePath}/${id}/reopen`)

export const deleteTask = (id: string) => apiSend<void>('DELETE', `${basePath}/${id}`)
