// Differential fuzzer: sends random (valid, edge-case and malformed) queries to a running API and checks
// every response against an independent oracle — a from-scratch re-implementation of the spec and the
// documented rules (README). Status, error fields, counts, ordering and scores must all match exactly.
// The oracle assumes the default Scoring options and the America/New_York market time zone.
//
// Usage (from the repo root, with the API running):
//   node tools/fuzz.mjs [baseUrl=http://localhost:5151] [count=5000] [seed=42] [listings=src/ListingSearch.Api/Data/sample_listings.json]
import { readFileSync } from 'node:fs'

const [, , baseUrl = 'http://localhost:5151', countArg = '5000', seedArg = '42',
  dataPath = 'src/ListingSearch.Api/Data/sample_listings.json'] = process.argv
const listings = JSON.parse(readFileSync(dataPath, 'utf8'))
const COUNT = Number(countArg)

// ---- deterministic PRNG ----
let seed = Number(seedArg) >>> 0
const rand = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 2 ** 32)
const pick = arr => arr[Math.floor(rand() * arr.length)]
const int = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1))

// ---- oracle ----
const SUPPORTED = ['minPrice', 'maxPrice', 'minBedrooms', 'city', 'keyword', 'targetBudget', 'page', 'pageSize']
const DEC_MAX = 79228162514264337593543950335n
const INT_MAX = 2147483647n, INT_MIN = -2147483648n
const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/New_York' }).format(new Date())
const dayNumber = iso => Math.floor(Date.UTC(+iso.slice(0, 4), +iso.slice(5, 7) - 1, +iso.slice(8, 10)) / 86400000)
const knownCities = [...new Set(listings.map(l => l.city.trim().toLowerCase()))]

const roundHalfEven = (x, digits) => {
  const f = 10 ** digits, y = x * f, r = Math.round(y)
  return (Math.abs(y % 1) === 0.5 ? (r % 2 === 0 ? r : r - 1) : r) / f
}

function parseDecimal(v) {
  if (!/^[+-]?(\d+\.?\d*|\.\d+)$/.test(v)) return { err: 'nan' }
  const intPart = v.replace(/^[+-]/, '').split('.')[0] || '0'
  if (BigInt(intPart) > DEC_MAX) return { err: 'large' }
  return { value: Number(v) }
}
function parseInt32(v) {
  if (!/^[+-]?\d+$/.test(v)) return { err: 'nan' }
  const b = BigInt(v)
  if (b > INT_MAX || b < INT_MIN) return { err: 'large' }
  return { value: Number(v) }
}

function oracle(params /* Map name -> string[] */) {
  const errors = new Set()
  for (const name of params.keys()) if (!SUPPORTED.some(s => s.toLowerCase() === name.toLowerCase())) errors.add(name)
  const get = name => {
    const vals = [...params.entries()].filter(([k]) => k.toLowerCase() === name.toLowerCase()).flatMap(([, v]) => v)
    if (vals.length === 0) return null
    if (vals.length > 1) { errors.add(name); return null } // repeated parameter
    const value = vals[0].trim()
    return value === '' ? null : value
  }
  const dec = name => { const v = get(name); if (v === null) return null; const r = parseDecimal(v); if (r.err) { errors.add(name); return null } return r.value }
  const i32 = name => { const v = get(name); if (v === null) return null; const r = parseInt32(v); if (r.err) { errors.add(name); return null } return r.value }

  const minPrice = dec('minPrice'), maxPrice = dec('maxPrice'), targetBudget = dec('targetBudget')
  const minBedrooms = i32('minBedrooms'), page = i32('page'), pageSize = i32('pageSize')
  if (minPrice !== null && minPrice < 0) errors.add('minPrice')
  if (maxPrice !== null && maxPrice < 0) errors.add('maxPrice')
  if (minPrice !== null && maxPrice !== null && minPrice > maxPrice) { errors.add('minPrice'); errors.add('maxPrice') }
  if (targetBudget !== null && targetBudget <= 0) errors.add('targetBudget')
  if (minBedrooms !== null && minBedrooms < 0) errors.add('minBedrooms')
  if (page !== null && page < 1) errors.add('page')
  if (pageSize !== null && (pageSize < 1 || pageSize > 50)) errors.add('pageSize')
  const keyword = get('keyword')
  if (keyword !== null && keyword.length > 200) errors.add('keyword')
  const city = get('city')
  if (city !== null && !knownCities.includes(city.toLowerCase())) errors.add('city')
  if (errors.size) return { status: 400, errors: [...errors].sort() }

  const p = page ?? 1, size = pageSize ?? 10
  const terms = keyword ? keyword.split(/\s+/).filter(Boolean).map(t => t.toLowerCase()) : []
  const matches = listings.filter(l =>
    (minPrice === null || l.price >= minPrice) && (maxPrice === null || l.price <= maxPrice) &&
    (minBedrooms === null || l.bedrooms >= minBedrooms) &&
    (city === null || l.city.trim().toLowerCase() === city.toLowerCase()) &&
    terms.every(t => l.description.toLowerCase().includes(t)))

  const scored = matches.map(l => {
    const age = Math.max(0, dayNumber(today) - dayNumber(l.listedDate))
    const recency = 0.5 ** (age / 30)
    let total
    if (targetBudget !== null) {
      let d = Math.abs(l.price - targetBudget) / targetBudget
      if (l.price > targetBudget) d *= 1.5
      const budget = Math.max(0, 1 - d / 0.5)
      total = (0.6 * budget + 0.4 * recency) / 1.0
    } else total = recency
    return { l, score: roundHalfEven(total * 100, 1) }
  })
  const cmp = (a, b) => b.score - a.score || dayNumber(b.l.listedDate) - dayNumber(a.l.listedDate) ||
    a.l.price - b.l.price || (a.l.source < b.l.source ? -1 : a.l.source > b.l.source ? 1 : 0) ||
    (a.l.id < b.l.id ? -1 : a.l.id > b.l.id ? 1 : 0)
  scored.sort(cmp)
  const totalPages = Math.ceil(scored.length / size)
  if (p > Math.max(totalPages, 1)) return { status: 400, errors: ['page'] }
  const items = scored.slice((p - 1) * size, p * size)
  return { status: 200, totalCount: scored.length, totalPages, page: p, pageSize: size,
    items: items.map(s => ({ key: `${s.l.source}:${s.l.id}`, score: s.score })) }
}

// ---- generators ----
const words = [...new Set(listings.flatMap(l => l.description.toLowerCase().match(/[a-z]+/g)))]
const recase = s => [...s].map(c => (rand() < 0.5 ? c.toUpperCase() : c.toLowerCase())).join('')
const gen = {
  minPrice: () => pick(['', '0', '399000', '450000', String(int(0, 800000)), String(int(0, 800000)), '-1', 'abc', '1,000', '1e6', ' 450000 ', '99999999999999999999999999999999', '0.5', '+400000', '.5']),
  maxPrice: () => pick(['', '0', '450000', '600000', String(int(0, 800000)), String(int(300000, 900000)), '-5', '$500', '500000.99']),
  minBedrooms: () => pick(['', '0', '1', '2', '3', '4', '5', String(int(0, 6)), '-1', '2.5', 'two', '99999999999']),
  city: () => pick(['', ...knownCities.map(recase), ...knownCities.map(c => `  ${c} `), 'Atlantis', 'Spring', 'reston,vienna']),
  keyword: () => pick(['', recase(pick(words)), `${pick(words)} ${pick(words)}`, '  pets  ', 'zzzz', 'a'.repeat(201), 'pet', 'NO PETS']),
  targetBudget: () => pick(['', '450000', '500000', String(int(100000, 900000)), String(int(1, 1000000)), '0', '-5', '0.0000000000000000000000000001', '475000.5', 'x']),
  page: () => pick(['', '1', '1', '2', '3', String(int(1, 15)), '0', '-1', 'x', '2147483647']),
  pageSize: () => pick(['', '1', '2', '5', '10', '50', String(int(1, 50)), '0', '51', '-3', 'abc']),
}

function randomQuery() {
  const params = new Map()
  for (const [name, g] of Object.entries(gen)) {
    if (rand() < 0.45) continue
    const key = rand() < 0.1 ? recase(name) : name
    params.set(key, [g()])
  }
  if (rand() < 0.05) params.set(pick(['max_price', 'status', 'foo']), ['1'])
  if (rand() < 0.04) { const k = pick(['minPrice', 'city', 'page']); params.set(k, [gen[k](), gen[k]()]) }
  if (rand() < 0.03) params.set('PAGESIZE', ['5']) // may collide case-insensitively with pageSize
  return params
}
const toQueryString = params => [...params].flatMap(([k, vs]) => vs.map(v => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`)).join('&')

// ---- run ----
const failures = []
const statusCounts = {}
let done = 0
async function runOne(params) {
  const qs = toQueryString(params)
  const expected = oracle(params)
  const res = await fetch(`${baseUrl}/api/listings/search?${qs}`)
  statusCounts[res.status] = (statusCounts[res.status] ?? 0) + 1
  const body = await res.json()
  const fail = msg => failures.push({ qs, msg, expected, actual: body })
  if (res.status !== expected.status) return fail(`status ${res.status} != ${expected.status}`)
  if (!(res.headers.get('content-type') ?? '').includes('json')) return fail('not JSON')
  if (res.status === 400) {
    const keys = Object.keys(body.errors ?? {}).sort()
    if (JSON.stringify(keys) !== JSON.stringify(expected.errors)) return fail(`error keys ${keys} != ${expected.errors}`)
    return
  }
  for (const f of ['totalCount', 'totalPages', 'page', 'pageSize'])
    if (body[f] !== expected[f]) return fail(`${f} ${body[f]} != ${expected[f]}`)
  const got = body.items.map(i => ({ key: `${i.source}:${i.id}`, score: i.score }))
  if (JSON.stringify(got.map(g => g.key)) !== JSON.stringify(expected.items.map(e => e.key))) return fail('item order differs')
  for (let i = 0; i < got.length; i++)
    if (Math.abs(got[i].score - expected.items[i].score) > 1e-9) return fail(`score ${got[i].score} != ${expected.items[i].score}`)
  if (body.items.some(i => i.score !== i.scoreBreakdown.total)) return fail('score != breakdown.total')
}

const queue = Array.from({ length: COUNT }, randomQuery)
const started = Date.now()
await Promise.all(Array.from({ length: 16 }, async () => {
  while (queue.length) { const q = queue.pop(); try { await runOne(q) } catch (e) { failures.push({ qs: toQueryString(q), msg: String(e) }) } done++ }
}))
console.log(JSON.stringify({ requests: done, ms: Date.now() - started, today, statusCounts, failures: failures.length, firstFailures: failures.slice(0, 5) }, null, 1))
process.exitCode = failures.length ? 1 : 0
