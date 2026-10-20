import { useEffect, useState } from 'react'
import { getCities, searchListings, ValidationError } from './api'
import { Pagination } from './components/Pagination'
import { ResultsTable } from './components/ResultsTable'
import { SearchForm } from './components/SearchForm'
import { emptyFilters, type FieldErrors, type SearchFilters, type SearchRequest, type SearchResponse } from './types'

/** The settled result of one specific request. */
type Outcome = { request: SearchRequest } & (
  | { status: 'success'; data: SearchResponse }
  | { status: 'error'; message: string; fieldErrors: FieldErrors }
)

const formFields = new Set(Object.keys(emptyFilters))

export default function App() {
  // The request is the single source of truth: every change to it triggers exactly one fetch.
  const [request, setRequest] = useState<SearchRequest>({ ...emptyFilters, page: 1, pageSize: 10 })
  const [outcome, setOutcome] = useState<Outcome | null>(null)
  const [cities, setCities] = useState<string[]>([])

  useEffect(() => {
    const controller = new AbortController()
    getCities(controller.signal).then(setCities).catch(() => { /* suggestions are optional */ })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    // Aborting on change means a slow, stale response can never overwrite a newer one.
    const controller = new AbortController()
    searchListings(request, controller.signal)
      .then(data => setOutcome({ request, status: 'success', data }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        setOutcome(error instanceof ValidationError
          ? { request, status: 'error', message: error.message, fieldErrors: error.fieldErrors }
          : { request, status: 'error', message: error instanceof Error ? error.message : 'Unexpected error.', fieldErrors: {} })
      })
    return () => controller.abort()
  }, [request])

  // Loading is derived, not stored: we're loading whenever the latest outcome is for an older request.
  const loading = outcome?.request !== request
  const error = !loading && outcome?.status === 'error' ? outcome : null
  // While loading, keep the previous page on screen (dimmed) to avoid layout flicker.
  const data = outcome?.status === 'success' ? outcome.data : null
  const fieldErrors = error?.fieldErrors ?? {}
  // Errors for parameters without a form input (e.g. page) are shown in the banner instead.
  const otherErrors = Object.entries(fieldErrors).filter(([field]) => !formFields.has(field)).flatMap(([, msgs]) => msgs)

  return (
    <main>
      <h1>Listing Search</h1>

      <SearchForm
        initial={emptyFilters}
        cities={cities}
        fieldErrors={fieldErrors}
        disabled={loading}
        onSearch={(filters: SearchFilters) => setRequest({ ...filters, page: 1, pageSize: request.pageSize })}
      />

      <section aria-live="polite" aria-busy={loading}>
        {loading && <p className="status">Searching…</p>}

        {error && (
          <div role="alert" className="error-banner">
            <strong>{error.message}</strong>
            {otherErrors.length > 0 && <ul>{otherErrors.map(msg => <li key={msg}>{msg}</li>)}</ul>}
          </div>
        )}

        {!loading && data?.totalCount === 0 && (
          <p className="status">No listings match your filters. Try widening the price range or removing the keyword.</p>
        )}

        {data && data.totalCount > 0 && (
          <div className={loading ? 'stale' : undefined}>
            <ResultsTable items={data.items} startRank={(data.page - 1) * data.pageSize + 1} />
            <Pagination
              page={data.page}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              pageSize={data.pageSize}
              disabled={loading}
              onPageChange={page => setRequest({ ...request, page })}
              onPageSizeChange={pageSize => setRequest({ ...request, page: 1, pageSize })}
            />
          </div>
        )}
      </section>
    </main>
  )
}
