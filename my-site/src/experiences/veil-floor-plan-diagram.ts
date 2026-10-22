import type { VeilArea, VeilLandmark, VeilTable } from './veil-floor-plan-data';

function tableFurniture(table: VeilTable, area: VeilArea): string {
  const x = table.x * area.width / 100;
  const y = table.y * area.height / 100;
  const width = table.width! * area.width / 100;
  const height = table.height! * area.height / 100;
  if (table.shape === 'round') return `
    <circle class="veil-furniture-shadow" cx="${x + 3}" cy="${y + 5}" r="${width / 2}" />
    <circle class="veil-table-footprint" cx="${x}" cy="${y}" r="${width / 2}" />
    <circle class="veil-table-inset" cx="${x}" cy="${y}" r="${width / 2 - 6}" />`;

  // Continuous upholstery bands illustrate lounge furniture without assigning
  // a number of chairs or a guest capacity to the table.
  const horizontal = width >= height;
  const bench = (offset: number) => {
    const bx = horizontal ? x - width / 2 + 4 : x + offset;
    const by = horizontal ? y + offset : y - height / 2 + 4;
    const bw = horizontal ? width - 8 : 30;
    const bh = horizontal ? 30 : height - 8;
    return `<g class="veil-lounge-bench">
      <rect x="${bx}" y="${by}" width="${bw}" height="${bh}" rx="10" />
      <rect class="veil-upholstery-inset" x="${bx + 5}" y="${by + 5}" width="${bw - 10}" height="${bh - 10}" rx="7" />
    </g>`;
  };
  return `${bench(-(horizontal ? height : width) / 2 - 40)}${bench((horizontal ? height : width) / 2 + 10)}
    <rect class="veil-furniture-shadow" x="${x - width / 2 + 3}" y="${y - height / 2 + 5}" width="${width}" height="${height}" rx="12" />
    <rect class="veil-table-footprint" x="${x - width / 2}" y="${y - height / 2}" width="${width}" height="${height}" rx="12" />
    <rect class="veil-table-inset" x="${x - width / 2 + 6}" y="${y - height / 2 + 6}" width="${width - 12}" height="${height - 12}" rx="8" />`;
}

function roomArtwork(room: VeilLandmark, area: VeilArea): string {
  const width = room.width * area.width / 100;
  const height = room.height * area.height / 100;
  const x = room.x * area.width / 100 - width / 2;
  const y = room.y * area.height / 100 - height / 2;
  let detail = '';
  if (room.kind === 'stairs') {
    detail = `<rect class="veil-room-hatch" x="${x}" y="${y}" width="${width}" height="${height}" />
      ${Array.from({ length: 8 }, (_, i) => `<path class="veil-stair-tread" d="M${x + 18} ${y + 20 + i * 30}H${x + width - 18}" />`).join('')}
      <path class="veil-stair-arrow" d="M${x + width - 30} ${y + height - 25}V${y + 25}l-10 15m10-15 10 15" />`;
  } else if (room.kind === 'entrance') {
    detail = `<rect class="veil-room-hatch" x="${x}" y="${y}" width="${width}" height="${height}" />
      <path class="veil-room-door" d="M${x + 35} ${y + height}V${y + height - 105}M${x + 35} ${y + height - 105}a105 105 0 0 1 105 105" />`;
  } else if (room.label === 'Бар' || room.label === 'Кальян') {
    detail = `<path class="veil-facility-counter" d="M${x + width - 38} ${y + 20}H${x + width - 14}V${y + height - 20}H${x + 20}V${y + height - 42}H${x + width - 38}Z" />`;
  } else {
    detail = `<path class="veil-room-divider" d="M${x + 15} ${y + height / 2}H${x + width - 15}" />`;
  }
  return `<g class="veil-facility-room">
    <rect class="veil-room-floor" x="${x}" y="${y}" width="${width}" height="${height}" rx="3" />
    ${detail}
  </g>`;
}

/** Architectural drawing and hit targets share one source of table geometry. */
export function veilHallDiagram(area: VeilArea): string {
  return `<svg class="veil-hall-diagram" viewBox="0 0 ${area.width} ${area.height}"
      width="${area.width}" height="${area.height}" aria-hidden="true">
    <defs>
      <linearGradient id="veil-table-surface" x2=".8" y2="1">
        <stop stop-color="#34312e" /><stop offset="1" stop-color="#23211f" />
      </linearGradient>
      <linearGradient id="veil-upholstery" x2="1" y2="1">
        <stop stop-color="#252321" /><stop offset="1" stop-color="#181716" />
      </linearGradient>
      <pattern id="veil-room-hatch" width="26" height="26" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
        <path d="M0 0V26" stroke="#bdb5aa" stroke-opacity=".1" stroke-width="4" />
      </pattern>
    </defs>
    <rect class="veil-hall-floor" x="40" y="40" width="${area.width - 80}" height="${area.height - 80}" />
    <path class="veil-room-wall" d="M40 ${area.height - 40}V40H${area.width - 40}V${area.height - 40}H210M80 ${area.height - 40}H40" />
    ${(area.landmarks ?? []).map((room) => roomArtwork(room, area)).join('')}
    ${area.tables.map((table) => `<g class="veil-table-furniture" data-furniture="${table.id}">${tableFurniture(table, area)}</g>`).join('')}
  </svg>`;
}
