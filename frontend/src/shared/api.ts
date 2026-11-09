import { auth } from '../features/auth/firebase'

/** RFC 7807 ProblemDetails body returned by the API on errors (`errors` is set for validation problems). */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  traceId?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null) {
    super(problem?.title ?? `API request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE'

/** GET an API path with the signed-in user's Firebase ID token. Throws ApiError on non-2xx. */
export async function apiGet<T>(path: string): Promise<T> {
  return (await request('GET', path)) as T
}

/** POST/PUT/DELETE an API path with an optional JSON body. Resolves to undefined for 204. Throws ApiError on non-2xx. */
export async function apiSend<T>(method: Exclude<Method, 'GET'>, path: string, body?: unknown): Promise<T> {
  return (await request(method, path, body)) as T
}

async function request(method: Method, path: string, body?: unknown): Promise<unknown> {
  const user = auth.currentUser
  if (!user) throw new ApiError(401, null)

  const token = await user.getIdToken()
  const headers: Record<string, string> = { Authorization: `Bearer ${token}` }
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const response = await fetch(path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (import.meta.env.DEV) {
    console.debug(`[api] ${method}`, { method, path, status: response.status })
  }

  if (!response.ok) throw new ApiError(response.status, await readProblem(response))
  if (response.status === 204) return undefined
  return await response.json()
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('json')) return null

  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return null
  }
}
