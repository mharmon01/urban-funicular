import type { ListingResult } from '../types'

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })

function scoreTooltip({ budget, recency }: ListingResult['scoreBreakdown']) {
  const parts = [`Recency ${recency.toFixed(3)}`]
  if (budget !== null) parts.unshift(`Budget ${budget.toFixed(3)}`)
  return parts.join(' · ')
}

export function ResultsTable({ items, startRank }: { items: ListingResult[]; startRank: number }) {
  return (
    <div className="results-wrap">
      <table className="results">
        <thead>
          <tr>
            <th>#</th>
            <th>Address</th>
            <th>City</th>
            <th className="num">Price</th>
            <th className="num">Beds</th>
            <th className="num">Baths</th>
            <th className="num">Sqft</th>
            <th>Listed</th>
            <th>Status</th>
            <th>Source</th>
            <th className="num">Score</th>
          </tr>
        </thead>
        <tbody>
          {items.map((listing, index) => (
            // id is only unique per source, so the key must include the source.
            <tr key={`${listing.source}:${listing.id}`} title={listing.description}>
              <td>{startRank + index}</td>
              <td>{listing.address}</td>
              <td>{listing.city}</td>
              <td className="num">{usd.format(listing.price)}</td>
              <td className="num">{listing.bedrooms}</td>
              <td className="num">{listing.bathrooms}</td>
              <td className="num">{listing.sqft.toLocaleString('en-US')}</td>
              <td>{listing.listedDate}</td>
              <td>{listing.status}</td>
              <td>{listing.source}</td>
              <td className="num" title={scoreTooltip(listing.scoreBreakdown)}>{listing.score.toFixed(1)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
