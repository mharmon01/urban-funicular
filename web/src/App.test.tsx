import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import App from './App'
import type { ListingResult, SearchResponse } from './types'

function listing(id: string, overrides: Partial<ListingResult> = {}): ListingResult {
  return {
    id, source: 'MLS_A', address: `${id} Main St`, city: 'Springfield', state: 'VA', zip: '22150',
    price: 450000, bedrooms: 2, bathrooms: 1.5, sqft: 980, listedDate: '2026-08-29', status: 'active',
    description: 'Bright condo', score: 94.1, scoreBreakdown: { budget: 1, recency: 0.85, total: 94.1 },
    ...overrides,
  }
}

function page(items: ListingResult[], overrides: Partial<SearchResponse> = {}): SearchResponse {
  return { items, page: 1, pageSize: 10, totalCount: items.length, totalPages: items.length ? 1 : 0, ...overrides }
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

/** Routes /cities to a fixed list and every search call to the next queued response. */
function mockApi(...searchResponses: Response[]) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
    if (String(input).includes('/cities')) return json(['Springfield'])
    const next = searchResponses.shift()
    if (!next) throw new Error(`Unexpected request ${String(input)}`)
    return next
  })
}

const searchUrls = (spy: ReturnType<typeof mockApi>) =>
  spy.mock.calls.map(([url]) => String(url)).filter(url => url.includes('/search'))

describe('App', () => {
  it('loads and renders ranked results with address, price, beds and score', async () => {
    mockApi(json(page([listing('A1')])))
    render(<App />)

    const row = (await screen.findByText('A1 Main St')).closest('tr')!
    expect(within(row).getByText('$450,000')).toBeInTheDocument()
    expect(within(row).getByText('2')).toBeInTheDocument()
    expect(within(row).getByText('94.1')).toBeInTheDocument()
    expect(screen.getByText(/Page 1 of 1 · 1 result$/)).toBeInTheDocument()
  })

  it('sends filters as query parameters and resets to page 1', async () => {
    const spy = mockApi(json(page([listing('A1')])), json(page([listing('A2')])))
    render(<App />)
    await screen.findByText('A1 Main St')

    await userEvent.type(screen.getByLabelText('Min price ($)'), '400000')
    await userEvent.type(screen.getByLabelText('City'), 'Reston')
    await userEvent.click(screen.getByRole('button', { name: 'Search' }))
    await screen.findByText('A2 Main St')

    expect(searchUrls(spy)[1]).toBe('/api/listings/search?minPrice=400000&city=Reston&page=1&pageSize=10')
  })

  it('a new search starts from page 1 even when viewing a later page', async () => {
    const spy = mockApi(
      json(page([listing('A1')], { pageSize: 1, totalCount: 3, totalPages: 3 })),
      json(page([listing('A2')], { page: 2, pageSize: 1, totalCount: 3, totalPages: 3 })),
      json(page([listing('A3')])),
    )
    render(<App />)
    await screen.findByText('A1 Main St')
    await userEvent.click(screen.getByRole('button', { name: 'Next ›' }))
    await screen.findByText('A2 Main St')

    await userEvent.type(screen.getByLabelText('City'), 'Reston')
    await userEvent.click(screen.getByRole('button', { name: 'Search' }))

    await screen.findByText('A3 Main St')
    expect(searchUrls(spy)[2]).toContain('city=Reston&page=1')
  })

  it('shows API validation errors next to the offending field', async () => {
    mockApi(
      json(page([listing('A1')])),
      json({ title: 'One or more validation errors occurred.', errors: { minPrice: ['minPrice (9) cannot be greater than maxPrice (1).'] } }, 400),
    )
    render(<App />)
    await screen.findByText('A1 Main St')

    await userEvent.click(screen.getByRole('button', { name: 'Search' }))

    expect(await screen.findByText(/cannot be greater than maxPrice/)).toBeInTheDocument()
    expect(screen.getByLabelText('Min price ($)')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByRole('alert')).toHaveTextContent('Please fix the highlighted fields.')
  })

  it('shows a no-results message when nothing matches', async () => {
    mockApi(json(page([])))
    render(<App />)

    expect(await screen.findByText(/No listings match your filters/)).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('shows an error banner when the server is unreachable', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))
    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not reach the server')
  })

  it('reset clears the form and searches without filters', async () => {
    const spy = mockApi(json(page([listing('A1')])), json(page([listing('A2')])), json(page([listing('A3')])))
    render(<App />)
    await screen.findByText('A1 Main St')
    await userEvent.type(screen.getByLabelText('Keyword (description)'), 'pool')
    await userEvent.click(screen.getByRole('button', { name: 'Search' }))
    await screen.findByText('A2 Main St')

    await userEvent.click(screen.getByRole('button', { name: 'Reset' }))

    await screen.findByText('A3 Main St')
    expect(screen.getByLabelText('Keyword (description)')).toHaveValue('')
    expect(searchUrls(spy)[2]).toBe('/api/listings/search?page=1&pageSize=10')
  })

  it('changing page size goes back to page 1', async () => {
    const spy = mockApi(
      json(page([listing('A1')], { page: 2, pageSize: 10, totalCount: 12, totalPages: 2 })),
      json(page([listing('A2')], { page: 1, pageSize: 5, totalCount: 12, totalPages: 3 })),
    )
    render(<App />)
    await screen.findByText('A1 Main St')

    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), '5')

    await screen.findByText('A2 Main St')
    expect(searchUrls(spy)[1]).toContain('page=1&pageSize=5')
  })

  it('jumps to a page by number and collapses distant pages', async () => {
    const spy = mockApi(
      json(page([listing('A1')], { pageSize: 1, totalCount: 20, totalPages: 20 })),
      json(page([listing('A9')], { page: 9, pageSize: 1, totalCount: 20, totalPages: 20 })),
    )
    render(<App />)
    await screen.findByText('A1 Main St')
    expect(screen.getAllByText('…')).toHaveLength(1)
    expect(screen.queryByRole('button', { name: '10' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: '20' }))
    expect(searchUrls(spy)[1]).toContain('page=20')
  })

  it('shows the server message for unexpected errors', async () => {
    mockApi(json({ title: 'An error occurred while processing your request.', status: 500 }, 500))
    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent('An error occurred while processing your request.')
  })

  it('falls back to the status code when the error has no JSON body', async () => {
    mockApi(new Response('upstream down', { status: 502 }))
    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Request failed with status 502.')
  })

  it('lists errors for parameters without an input in the banner', async () => {
    mockApi(json({ errors: { page: ['page 9 is out of range; this search has 2 page(s).'] } }, 400))
    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent('page 9 is out of range')
  })

  it('explains the score breakdown in a tooltip', async () => {
    mockApi(json(page([
      listing('A1'),
      listing('A2', { score: 40, scoreBreakdown: { budget: null, recency: 0.4, total: 40 } }),
    ])))
    render(<App />)

    expect(await screen.findByText('94.1')).toHaveAttribute('title', 'Budget 1.000 · Recency 0.850')
    expect(screen.getByText('40.0')).toHaveAttribute('title', 'Recency 0.400')
  })

  it('moves between pages', async () => {
    const spy = mockApi(
      json(page([listing('A1')], { pageSize: 1, totalCount: 2, totalPages: 2 })),
      json(page([listing('A2')], { page: 2, pageSize: 1, totalCount: 2, totalPages: 2 })),
      json(page([listing('A1')], { pageSize: 1, totalCount: 2, totalPages: 2 })),
    )
    render(<App />)
    await screen.findByText('A1 Main St')
    expect(screen.getByRole('button', { name: '‹ Prev' })).toBeDisabled()

    await userEvent.click(screen.getByRole('button', { name: 'Next ›' }))

    expect(await screen.findByText('A2 Main St')).toBeInTheDocument()
    expect(screen.getByText(/Page 2 of 2/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next ›' })).toBeDisabled()
    expect(searchUrls(spy)[1]).toContain('page=2')

    await userEvent.click(screen.getByRole('button', { name: '‹ Prev' }))

    expect(await screen.findByText('A1 Main St')).toBeInTheDocument()
    expect(searchUrls(spy)[2]).toContain('page=1')
  })
})
