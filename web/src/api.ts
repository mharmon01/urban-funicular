import type { FieldErrors, SearchRequest, SearchResponse } from './types'

/** The API rejected the input (HTTP 400 ValidationProblem); `fieldErrors` is keyed by query parameter. */
export class ValidationError extends Error {
  readonly fieldErrors: FieldErrors

  constructor(fieldErrors: FieldErrors) {
    super('Please fix the highlighted fields.')
    this.fieldErrors = fieldErrors
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: FieldErrors
}

export async function searchListings(request: SearchRequest, signal?: AbortSignal): Promise<SearchResponse> {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(request)) {
    const text = String(value).trim()
    if (text !== '') params.set(key, text)
  }
  return getJson<SearchResponse>(`/api/listings/search?${params}`, signal)
}

export function getCities(signal?: AbortSignal): Promise<string[]> {
  return getJson<string[]>('/api/listings/cities', signal)
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, { signal })
  } catch (error) {
    if (signal?.aborted) throw error
    throw new Error('Could not reach the server. Is the API running?')
  }

  if (response.ok) return (await response.json()) as T

  const problem = await response.json().catch(() => null) as ProblemDetails | null
  if (response.status === 400 && problem?.errors) throw new ValidationError(problem.errors)
  throw new Error(problem?.detail ?? problem?.title ?? `Request failed with status ${response.status}.`)
}
