import { pageWindow } from '../pageWindow'

interface Props {
  page: number
  totalPages: number
  totalCount: number
  pageSize: number
  disabled: boolean
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
}

const pageSizes = [5, 10, 20, 50]

export function Pagination({ page, totalPages, totalCount, pageSize, disabled, onPageChange, onPageSizeChange }: Props) {
  return (
    <nav className="pagination" aria-label="Pagination">
      <button onClick={() => onPageChange(page - 1)} disabled={disabled || page <= 1}>‹ Prev</button>
      {pageWindow(page, totalPages).map((p, i) => p === 'gap' ? (
        <span key={`gap-${i}`} aria-hidden="true">…</span>
      ) : (
        <button
          key={p}
          onClick={() => onPageChange(p)}
          disabled={disabled || p === page}
          aria-current={p === page ? 'page' : undefined}
        >
          {p}
        </button>
      ))}
      <button onClick={() => onPageChange(page + 1)} disabled={disabled || page >= totalPages}>Next ›</button>
      <span className="page-info">
        Page {totalPages === 0 ? 0 : page} of {totalPages} · {totalCount} result{totalCount === 1 ? '' : 's'}
      </span>
      <label>
        Per page{' '}
        <select value={pageSize} onChange={e => onPageSizeChange(Number(e.target.value))} disabled={disabled}>
          {pageSizes.map(size => <option key={size} value={size}>{size}</option>)}
        </select>
      </label>
    </nav>
  )
}
