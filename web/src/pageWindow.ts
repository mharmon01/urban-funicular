export type PageItem = number | 'gap'

/**
 * Page numbers to render: always the first and last page, plus `radius` pages either side of the
 * current one, with 'gap' (rendered as …) for skipped runs. A gap that would hide a single page
 * shows that page instead, since "…" is no shorter than the number it replaces.
 * e.g. pageWindow(10, 20) → [1, 'gap', 8, 9, 10, 11, 12, 'gap', 20]
 */
export function pageWindow(page: number, totalPages: number, radius = 2): PageItem[] {
  if (totalPages < 1) return []

  const pages = new Set([1, totalPages])
  for (let p = Math.max(1, page - radius); p <= Math.min(totalPages, page + radius); p++) pages.add(p)

  const items: PageItem[] = []
  let previous = 0
  for (const p of [...pages].sort((a, b) => a - b)) {
    if (p - previous === 2) items.push(previous + 1)
    else if (p - previous > 2) items.push('gap')
    items.push(p)
    previous = p
  }
  return items
}
