// Concurrency smoke test: N requests with C in flight; every response must be a 200 and byte-identical
// (shared in-memory state must be safe under concurrent reads).
// Usage (from the repo root, with the API running): node tools/load.mjs [baseUrl=http://localhost:5151] [total=20000] [concurrency=200]
const [, , baseUrl = 'http://localhost:5151', total = '20000', concurrency = '200'] = process.argv
const url = `${baseUrl}/api/listings/search?targetBudget=500000&pageSize=50`
const reference = await (await fetch(url)).text()
let remaining = Number(total), mismatches = 0, errors = 0
const latencies = []
const started = performance.now()
await Promise.all(Array.from({ length: Number(concurrency) }, async () => {
  while (remaining-- > 0) {
    const t = performance.now()
    try {
      const res = await fetch(url)
      const body = await res.text()
      if (res.status !== 200) errors++
      else if (body !== reference) mismatches++
    } catch { errors++ }
    latencies.push(performance.now() - t)
  }
}))
latencies.sort((a, b) => a - b)
const pct = p => latencies[Math.floor(latencies.length * p)].toFixed(1)
console.log(JSON.stringify({
  requests: latencies.length, errors, mismatches,
  rps: Math.round(latencies.length / ((performance.now() - started) / 1000)),
  p50ms: pct(0.5), p95ms: pct(0.95), p99ms: pct(0.99),
}))
