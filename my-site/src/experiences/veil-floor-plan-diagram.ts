import type { VeilArea } from './veil-floor-plan-data';

/** Render the seating sketch from the same positions as its interactive markers. */
export function veilHallDiagram(area: VeilArea): string {
  return `<svg class="veil-hall-diagram" viewBox="0 0 ${area.width} ${area.height}"
      width="${area.width}" height="${area.height}" aria-hidden="true">
    <path class="veil-room-wall" d="M30 1140V30H1010V1370H30V1330" />
    <path class="veil-room-door" d="M30 1330H220M220 1330A190 190 0 0 0 30 1140" />
    ${area.tables.map((table) => {
      const width = table.width! * area.width / 100;
      const height = table.height! * area.height / 100;
      return `<rect class="veil-table-footprint" x="${table.x * area.width / 100 - width / 2}"
        y="${table.y * area.height / 100 - height / 2}" width="${width}" height="${height}" rx="12" />`;
    }).join('')}
  </svg>`;
}
