export interface VeilTable {
  /** UI identity only; map to tenant inventory when Turso is configured. */
  id: string;
  number: number;
  capacity: number | null;
  x: number;
  y: number;
  width?: number;
  height?: number;
}

export interface VeilLandmark {
  label: string;
  kind: 'facility' | 'vip' | 'entrance';
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
    description: 'Вход — внизу слева. Бар и кальян — справа, М/Ж — вверху слева.',
    tables: hallPositions.map(([number, x, y, width, height]) => ({
      id: `hall-${number}`, number, capacity: null,
      ...rotatedBox(x, y, width, height),
    })),
    landmarks: [
      { label: 'VIP 1', kind: 'vip', ...rotatedBox(60, 50, 150, 150) },
      { label: 'VIP 2', kind: 'vip', ...rotatedBox(210, 50, 110, 150) },
      { label: 'Бар', kind: 'facility', ...rotatedBox(410, 50, 420, 150) },
      { label: 'Кальян', kind: 'facility', ...rotatedBox(830, 50, 290, 150) },
      { label: 'М/Ж', kind: 'facility', ...rotatedBox(65, 890, 220, 100) },
      { label: 'Вход →', kind: 'entrance', x: 10, y: 89, width: 18, height: 7 },
    ],
  },
  area('vip', 'VIP', 1900, 920, 440, [
    [28, 35.961, 35.217], [29, 79.013, 72.527],
  ]),
];
