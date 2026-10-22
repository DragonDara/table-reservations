export interface VeilTable {
  /** UI identity only; map to tenant inventory when Turso is configured. */
  id: string;
  number: number;
  capacity: number | null;
  shape?: 'round' | 'rectangular';
  x: number;
  y: number;
  width?: number;
  height?: number;
}

export interface VeilLandmark {
  label: string;
  kind: 'facility' | 'stairs' | 'entrance';
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface VeilArea {
  id: string;
  label: string;
  image?: string;
  description: string;
  width: number;
  height: number;
  minMapWidth: number;
  tables: VeilTable[];
  landmarks?: VeilLandmark[];
}

function area(
  id: string,
  label: string,
  width: number,
  height: number,
  minMapWidth: number,
  positions: [number, number, number][],
): VeilArea {
  return {
    id, label, width, height, minMapWidth,
    image: `/tenants/secret-veil/floor-plans/${id}.png`,
    description: 'Перемещайте схему, чтобы рассмотреть столики',
    tables: positions.map(([number, x, y]) => ({
      id: `${id}-${number}`, number, x, y, capacity: null,
    })),
  };
}

// Coordinates follow the newly supplied handwritten seating chart. Rotate it
// clockwise so the entrance stays at the lower left, as the venue clarified.
// This is a schematic: neither table dimensions nor capacities are inferred.
function rotatedBox(x: number, y: number, width: number, height: number) {
  return {
    x: 100 * (1040 - y - height / 2) / 1040,
    y: 100 * (x + width / 2) / 1400,
    width: 100 * height / 1040,
    height: 100 * width / 1400,
  };
}

const hallPositions: [number, number, number, number, number][] = [
  // Number, x, y, width, height on the handwritten chart before rotation.
  [1, 1240, 75, 100, 120], [2, 1240, 245, 100, 120],
  [3, 1240, 415, 100, 120], [4, 1240, 585, 100, 130],
  [5, 930, 890, 150, 100], [6, 700, 890, 150, 100],
  [7, 470, 890, 150, 100], [8, 480, 540, 150, 110],
  [9, 480, 395, 150, 105], [10, 720, 395, 150, 105],
  [11, 720, 540, 150, 110], [12, 510, 280, 75, 75],
  [13, 660, 280, 75, 75], [14, 810, 280, 75, 75],
  [15, 1100, 415, 70, 70], [16, 910, 400, 60, 80],
  [17, 1100, 530, 70, 70], [18, 910, 550, 60, 80],
  [19, 1100, 645, 70, 70], [20, 760, 680, 70, 70],
  [21, 520, 680, 70, 70], [22, 270, 665, 80, 80],
  [23, 270, 410, 80, 80], [24, 70, 600, 110, 150],
  [25, 70, 395, 110, 150],
];

// Seasonal outdoor areas are omitted from the active picker. VIP artwork and
// table identities remain unchanged until the venue confirms room booking rules.
export const veilAreas: VeilArea[] = [
  {
    id: 'hall', label: 'Зал', width: 1040, height: 1400, minMapWidth: 330,
    description: 'Вход — внизу слева. Лестница к VIP на 2-й этаж — вверху справа.',
    tables: hallPositions.map(([number, x, y, width, height]) => {
      const round = number >= 12 && number <= 23;
      return {
        id: `hall-${number}`, number, capacity: null,
        ...rotatedBox(x, y, width, height),
        shape: round ? 'round' : 'rectangular',
        // Equal rendered width/height for the small round tables. These are
        // diagram units only, not physical dimensions or inferred seat counts.
        ...(round ? { width: 100 * 72 / 1040, height: 100 * 72 / 1400 } : {}),
      };
    }),
    landmarks: [
      { label: 'Лестница к VIP · 2‑й этаж', kind: 'stairs', x: 88, y: 11, width: 18, height: 16 },
      { label: 'Бар', kind: 'facility', x: 88, y: 36, width: 18, height: 21 },
      { label: 'Кальян', kind: 'facility', x: 88, y: 58, width: 18, height: 23 },
      { label: 'М/Ж', kind: 'facility', ...rotatedBox(65, 890, 220, 100) },
      { label: 'Вход', kind: 'entrance', x: 12.5, y: 89.3, width: 19.2, height: 17.1 },
    ],
  },
  area('vip', 'VIP', 1900, 920, 440, [
    [28, 35.961, 35.217], [29, 79.013, 72.527],
  ]),
];
