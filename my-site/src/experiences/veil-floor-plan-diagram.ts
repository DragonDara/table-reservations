import type { VeilArea, VeilLandmark, VeilTable } from './veil-floor-plan-data';

function upholsteredBench(x: number, y: number, width: number, height: number): string {
  const vertical = height > width;
  const length = vertical ? height : width;
  const depth = vertical ? width : height;
  const transform = vertical ? `translate(${x + width} ${y}) rotate(90)` : `translate(${x} ${y})`;
  return `<g class="veil-lounge-bench" transform="${transform}">
    <rect class="veil-sofa-body" width="${length}" height="${depth}" rx="11" />
    <path class="veil-sofa-back" d="M10 8Q${length / 2} 3 ${length - 10} 8V15Q${length / 2} 10 10 15Z" />
    <rect class="veil-sofa-cushion" x="10" y="12" width="${length - 20}" height="${depth - 16}" rx="5" />
    <path class="veil-sofa-arms" d="M9 5Q3 14 8 ${depth - 4}H15Q10 14 15 6ZM${length - 9} 5Q${length - 3} 14 ${length - 8} ${depth - 4}H${length - 15}Q${length - 10} 14 ${length - 15} 6Z" />
    <path class="veil-upholstery-stitch" d="M20 ${depth - 7}H${length - 20}" />
    <path class="veil-upholstery-crease" d="M18 16q4 2 7 0m${length - 50} 0q4 2 7 0" />
  </g>`;
}

function tableFurniture(table: VeilTable, area: VeilArea): string {
  const x = table.x * area.width / 100;
  const y = table.y * area.height / 100;
  const width = table.width! * area.width / 100;
  const height = table.height! * area.height / 100;
  if (table.shape === 'round') return `
    <circle class="veil-furniture-shadow" cx="${x + 3}" cy="${y + 5}" r="${width / 2}" />
    <circle class="veil-table-footprint" cx="${x}" cy="${y}" r="${width / 2}" />
    <circle class="veil-table-grain" cx="${x}" cy="${y}" r="${width / 2 - 4}" />
    <circle class="veil-table-inset" cx="${x}" cy="${y}" r="${width / 2 - 6}" />
    <path class="veil-table-glint" d="M${x - width * .29} ${y - width * .27}a${width * .38} ${width * .38} 0 0 1 ${width * .55} 0" />`;

  // Continuous upholstery bands illustrate lounge furniture without assigning
  // a number of chairs or a guest capacity to the table.
  const horizontal = width >= height;
  const bench = (offset: number) => {
    const bx = horizontal ? x - width / 2 + 4 : x + offset;
    const by = horizontal ? y + offset : y - height / 2 + 4;
    const bw = horizontal ? width - 8 : 30;
    const bh = horizontal ? 30 : height - 8;
    return upholsteredBench(bx, by, bw, bh);
  };
  return `${bench(-(horizontal ? height : width) / 2 - 40)}${bench((horizontal ? height : width) / 2 + 10)}
    <rect class="veil-furniture-shadow" x="${x - width / 2 + 3}" y="${y - height / 2 + 5}" width="${width}" height="${height}" rx="12" />
    <rect class="veil-table-footprint" x="${x - width / 2}" y="${y - height / 2}" width="${width}" height="${height}" rx="12" />
    <rect class="veil-table-grain" x="${x - width / 2 + 4}" y="${y - height / 2 + 4}" width="${width - 8}" height="${height - 8}" rx="9" />
    <rect class="veil-table-inset" x="${x - width / 2 + 6}" y="${y - height / 2 + 6}" width="${width - 12}" height="${height - 12}" rx="8" />
    <path class="veil-table-glint" d="M${x - width / 2 + 6} ${y - height / 2 + 22}q0-16 16-16h${width - 44}" />
    <g class="veil-table-accessories" transform="translate(${x + width / 2 - 31} ${y - height / 2 + 16}) rotate(10)">
      <rect width="16" height="23" rx="2" /><path d="M4 7h8M4 11h8M4 15h5" />
    </g>
    <g class="veil-table-vase" transform="translate(${x - width / 2 + 22} ${y - height / 2 + 22})">
      <circle r="6" /><path d="M0-2q-10-12-10-3t10 3q8-14 10-6T0-2" />
    </g>`;
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
    detail += `<path class="veil-stair-rail" d="M${x + 10} ${y + 16}V${y + height - 16}H${x + width - 10}V${y + 16}" />`;
  } else if (room.kind === 'entrance') {
    detail = `<rect class="veil-room-hatch" x="${x}" y="${y}" width="${width}" height="${height}" />
      <path class="veil-room-door" d="M${x + 35} ${y + height}V${y + height - 105}M${x + 35} ${y + height - 105}a105 105 0 0 1 105 105" />`;
    detail += `<rect class="veil-entrance-mat" x="${x + 65}" y="${y + height - 85}" width="105" height="45" rx="4" />
      <path class="veil-threshold" d="M${x + 30} ${y + height - 5}h115m-115 10h115" />`;
  } else if (room.label === 'Бар' || room.label === 'Кальян') {
    detail = `<path class="veil-facility-counter" d="M${x + width - 38} ${y + 20}H${x + width - 14}V${y + height - 20}H${x + 20}V${y + height - 42}H${x + width - 38}Z" />`;
    detail += `<path class="veil-counter-reveal" d="M${x + width - 33} ${y + 24}V${y + height - 28}H${x + 24}" />
      ${Array.from({ length: room.label === 'Бар' ? 3 : 5 }, (_, i) => `<path class="veil-cabinet-joint" d="M${x + width - 37} ${y + 55 + i * 70}h22" />`).join('')}`;
    if (room.label === 'Бар') {
      detail += `<g class="veil-bar-station" transform="translate(${x + 25} ${y + 26})">
        <rect width="54" height="38" rx="3" /><rect x="7" y="7" width="40" height="24" rx="5" />
        <path d="M27 9V-4q0-10 9-10t9 10" />
        <circle cx="80" cy="18" r="8" /><circle cx="102" cy="18" r="8" />
      </g>`;
    } else {
      detail += `<g class="veil-hookah-detail" transform="translate(${x + 57} ${y + 85})">
        <path d="M-8-24H8l-3 16H-5ZM0-8V40M-19 9q19 10 38 0M-8 40q-18 22-12 37t40 0-12-37Z" />
        <path d="M8 44q39-2 29-31t12-30" />
        <ellipse cx="0" cy="78" rx="20" ry="5" />
      </g>`;
    }
  } else {
    detail = `<path class="veil-room-divider" d="M${x + 15} ${y + height / 2}H${x + width - 15}" />`;
    detail += `<g class="veil-washroom-detail" transform="translate(${x + 35} ${y + 32})">
      <rect width="40" height="32" rx="7" /><ellipse cx="20" cy="17" rx="12" ry="8" /><path d="M20 8V-3h10" />
      <path d="M65 10h30v34q-15 23-30 0Z" /><path d="M71 24q9-9 18 0v15q-9 14-18 0Z" />
    </g>`;
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
        <stop stop-color="#4b453e" /><stop offset=".4" stop-color="#36322e" /><stop offset="1" stop-color="#282522" />
      </linearGradient>
      <linearGradient id="veil-upholstery" x2="1" y2="1">
        <stop stop-color="#3c3731" /><stop offset="1" stop-color="#201e1b" />
      </linearGradient>
      <pattern id="veil-room-hatch" width="26" height="26" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
        <path d="M0 0V26" stroke="#bdb5aa" stroke-opacity=".1" stroke-width="4" />
      </pattern>
      <pattern id="veil-wood-grain" width="34" height="80" patternUnits="userSpaceOnUse" patternTransform="rotate(8)">
        <path d="M4 0q8 20 0 40T4 80M18 0q-6 25 0 43t0 37M29 0q5 23 0 45t0 35" fill="none" stroke="#c7b59d" stroke-width="1" stroke-opacity=".17" />
      </pattern>
      <pattern id="veil-floor-boards" width="150" height="240" patternUnits="userSpaceOnUse">
        <rect width="150" height="240" fill="#0f0e0d" />
        <path d="M0 0V240M75 0V240M0 100H75M75 210h75" fill="none" stroke="#8b7863" stroke-width="1" stroke-opacity=".075" />
      </pattern>
      <filter id="veil-soft-shadow" x="-25%" y="-25%" width="160%" height="160%"><feGaussianBlur stdDeviation="3" /></filter>
    </defs>
    <rect class="veil-hall-floor" x="40" y="40" width="${area.width - 80}" height="${area.height - 80}" />
    <path class="veil-room-wall" d="M40 ${area.height - 40}V40H${area.width - 40}V${area.height - 40}H210M80 ${area.height - 40}H40" />
    <path class="veil-wall-reveal" d="M53 ${area.height - 54}V53H${area.width - 53}V${area.height - 53}H218" />
    ${(area.landmarks ?? []).map((room) => roomArtwork(room, area)).join('')}
    ${area.tables.map((table) => `<g class="veil-table-furniture" data-furniture="${table.id}">${tableFurniture(table, area)}</g>`).join('')}
  </svg>`;
}
