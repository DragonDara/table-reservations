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

const HALL_WIDTH = 1280;
const HALL_HEIGHT = 1900;

// The annotated chart establishes relative placement. Drawing units leave
// room for furniture and aisles; they are not measured sizes or seat counts.
function hallBox(x: number, y: number, width: number, height: number) {
  return {
    x: 100 * x / HALL_WIDTH,
    y: 100 * y / HALL_HEIGHT,
    width: 100 * width / HALL_WIDTH,
    height: 100 * height / HALL_HEIGHT,
  };
}

const hallPositions: [number, number, number, number, number][] = [
  // Number, center x/y, tabletop width/height in the upright drawing.
  [1, 1110, 1730, 150, 120], [2, 870, 1730, 150, 120],
  [3, 630, 1730, 150, 120], [4, 390, 1730, 150, 120],
  [5, 160, 1340, 120, 170], [6, 160, 995, 120, 170],
  [7, 160, 650, 120, 170], [8, 550, 710, 140, 170],
  [9, 800, 710, 140, 170], [10, 800, 1070, 140, 170],
  [11, 550, 1070, 140, 170], [12, 995, 650, 86, 86],
  [13, 995, 860, 86, 86], [14, 995, 1070, 86, 86],
  [15, 995, 1510, 86, 86], [16, 800, 1320, 86, 86],
  [17, 690, 1510, 86, 86], [18, 550, 1320, 86, 86],
  [19, 390, 1510, 86, 86], [20, 355, 1070, 86, 86],
  [21, 355, 710, 86, 86], [22, 390, 420, 86, 86],
  [23, 700, 420, 86, 86], [24, 390, 170, 150, 110],
  [25, 700, 170, 150, 110],
];

// Seasonal outdoor areas are omitted from the active picker. VIP artwork and
// table identities remain unchanged until the venue confirms room booking rules.
export const veilAreas: VeilArea[] = [
  {
    id: 'hall', label: 'Зал', width: HALL_WIDTH, height: HALL_HEIGHT, minMapWidth: 340,
    description: 'Вход — внизу слева. Лестница к VIP на 2-й этаж — вверху справа.',
    tables: hallPositions.map(([number, x, y, width, height]) => {
      const round = number >= 12 && number <= 23;
      return {
        id: `hall-${number}`, number, capacity: null,
        ...hallBox(x, y, width, height),
        shape: round ? 'round' : 'rectangular',
      };
    }),
    landmarks: [
      { label: 'Лестница к VIP · 2‑й этаж', kind: 'stairs', ...hallBox(1140, 210, 200, 260) },
      { label: 'Бар', kind: 'facility', ...hallBox(1160, 650, 160, 300) },
      { label: 'Кальян', kind: 'facility', ...hallBox(1160, 1110, 160, 460) },
      { label: 'М/Ж', kind: 'facility', ...hallBox(145, 210, 170, 260) },
      { label: 'Вход', kind: 'entrance', ...hallBox(160, 1720, 240, 280) },
    ],
  },
  area('vip', 'VIP', 1900, 920, 440, [
    [28, 35.961, 35.217], [29, 79.013, 72.527],
  ]),
];
