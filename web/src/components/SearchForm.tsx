import { useState, type FormEvent } from 'react'
import { emptyFilters, type FieldErrors, type SearchFilters } from '../types'

interface Props {
  initial: SearchFilters
  cities: string[]
  fieldErrors: FieldErrors
  disabled: boolean
  onSearch: (filters: SearchFilters) => void
}

const fields: { name: keyof SearchFilters; label: string; type: 'number' | 'text'; placeholder?: string }[] = [
  { name: 'minPrice', label: 'Min price ($)', type: 'number' },
  { name: 'maxPrice', label: 'Max price ($)', type: 'number' },
  { name: 'minBedrooms', label: 'Min bedrooms', type: 'number' },
  { name: 'city', label: 'City', type: 'text', placeholder: 'Any' },
  { name: 'keyword', label: 'Keyword (description)', type: 'text', placeholder: 'e.g. pool garage' },
  { name: 'targetBudget', label: 'Target budget ($)', type: 'number', placeholder: 'Ranks by closeness' },
]

export function SearchForm({ initial, cities, fieldErrors, disabled, onSearch }: Props) {
  const [filters, setFilters] = useState(initial)

  function submit(event: FormEvent) {
    event.preventDefault()
    onSearch(filters)
  }

  function reset() {
    setFilters(emptyFilters)
    onSearch(emptyFilters)
  }

  return (
    <form onSubmit={submit} className="search-form" noValidate>
      {fields.map(({ name, label, type, placeholder }) => {
        const errors = fieldErrors[name]
        const errorId = `${name}-error`
        return (
          <div key={name} className="field">
            <label htmlFor={name}>{label}</label>
            <input
              id={name}
              name={name}
              type={type}
              min={type === 'number' ? 0 : undefined}
              placeholder={placeholder}
              list={name === 'city' ? 'city-options' : undefined}
              value={filters[name]}
              onChange={e => setFilters({ ...filters, [name]: e.target.value })}
              aria-invalid={errors ? true : undefined}
              aria-describedby={errors ? errorId : undefined}
            />
            {errors && <span id={errorId} className="field-error">{errors.join(' ')}</span>}
          </div>
        )
      })}
      <datalist id="city-options">
        {cities.map(city => <option key={city} value={city} />)}
      </datalist>
      <div className="form-actions">
        <button type="submit" disabled={disabled}>Search</button>
        <button type="button" onClick={reset} disabled={disabled}>Reset</button>
      </div>
    </form>
  )
}
