// Mirrors the C# DTOs in ListingSearch.Api/Search (serialized as camelCase).

export interface ScoreBreakdown {
  budget: number | null
  recency: number
  total: number
}

export interface ListingResult {
  id: string
  source: string
  address: string
  city: string
  state: string
  zip: string
  price: number
  bedrooms: number
  bathrooms: number
  sqft: number
  listedDate: string
  status: string
  description: string
  score: number
  scoreBreakdown: ScoreBreakdown
}

export interface SearchResponse {
  items: ListingResult[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

/** Form values are kept as raw strings; the API owns parsing and validation. */
export interface SearchFilters {
  minPrice: string
  maxPrice: string
  minBedrooms: string
  city: string
  keyword: string
  targetBudget: string
}

export interface SearchRequest extends SearchFilters {
  page: number
  pageSize: number
}

export type FieldErrors = Record<string, string[]>

export const emptyFilters: SearchFilters = {
  minPrice: '',
  maxPrice: '',
  minBedrooms: '',
  city: '',
  keyword: '',
  targetBudget: '',
}
