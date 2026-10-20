import { describe, expect, it } from 'vitest'
import { pageWindow } from './pageWindow'

describe('pageWindow', () => {
  it('returns nothing when there are no pages', () => {
    expect(pageWindow(1, 0)).toEqual([])
  })

  it('shows every page when they all fit', () => {
    expect(pageWindow(1, 1)).toEqual([1])
    expect(pageWindow(3, 7)).toEqual([1, 2, 3, 4, 5, 6, 7])
  })

  it('collapses runs on both sides of the current page', () => {
    expect(pageWindow(10, 20)).toEqual([1, 'gap', 8, 9, 10, 11, 12, 'gap', 20])
  })

  it('collapses only the far side at the edges', () => {
    expect(pageWindow(1, 20)).toEqual([1, 2, 3, 'gap', 20])
    expect(pageWindow(20, 20)).toEqual([1, 'gap', 18, 19, 20])
  })

  it('shows a single skipped page instead of a gap', () => {
    expect(pageWindow(5, 20)).toEqual([1, 2, 3, 4, 5, 6, 7, 'gap', 20])
  })
})
