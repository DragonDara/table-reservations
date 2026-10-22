import type { VeilArea } from './veil-floor-plan-data';

/** Render the seating sketch from the same positions as its interactive markers. */
export function veilHallDiagram(area: VeilArea): string {
  return `<svg class="veil-hall-diagram" viewBox="0 0 ${area.width} ${area.height}"
      width="${area.width}" height="${area.height}" aria-hidden="true">
    <path class="veil-room-wall" d="M30 1370V30H1010V1370H200M90 1370H30" />
    <path class="veil-room-door" d="M90 1370V1260M90 1260A110 110 0 0 1 200 1370" />
    ${area.tables.map((table) => {
      const width = table.width! * area.width / 100;
      const height = table.height! * area.height / 100;
      if (table.shape === 'round') return `<circle class="veil-table-footprint" cx="${table.x * area.width / 100}"
        cy="${table.y * area.height / 100}" r="${width / 2}" />`;
      return `<rect class="veil-table-footprint" x="${table.x * area.width / 100 - width / 2}"
        y="${table.y * area.height / 100 - height / 2}" width="${width}" height="${height}" rx="12" />`;
    }).join('')}
  </svg>`;
}
