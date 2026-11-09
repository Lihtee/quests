import { auth } from '../features/auth/firebase'

/** RFC 7807 ProblemDetails body returned by the API on errors. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  traceId?: string
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

/** GET an API path with the signed-in user's Firebase ID token. Throws ApiError on non-2xx. */
export async function apiGet<T>(path: string): Promise<T> {
  const user = auth.currentUser
  if (!user) throw new ApiError(401, null)

  const token = await user.getIdToken()
  const response = await fetch(path, { headers: { Authorization: `Bearer ${token}` } })

  if (import.meta.env.DEV) {
    console.debug('[api] GET', { path, status: response.status })
  }

  if (!response.ok) throw new ApiError(response.status, await readProblem(response))
  return (await response.json()) as T
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
