export interface VeilTable {
  /** UI identity only; map to tenant inventory when Turso is configured. */
  id: string;
  number: number;
  capacity: number | null;
  x: number;
  y: number;
}

export interface VeilArea {
  id: string;
  label: string;
  image: string;
  width: number;
  height: number;
  minMapWidth: number;
  tables: VeilTable[];
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
    tables: positions.map(([number, x, y]) => ({
      id: `${id}-${number}`, number, x, y, capacity: null,
    })),
  };
}

// Hall/terrace/VIP positions come from the supplied SVG table groups. The
// portrait terrace/VIP coordinates are rotated back to their PNG orientation.
// Numbering follows the full plans; the separate detail images differ.
// Balcony has its own inventory even where its display numbers repeat.
export const veilAreas: VeilArea[] = [
  area('hall', 'Зал', 1510, 1440, 440, [
    [14, 7.682, 14.132], [15, 9.603, 29.549], [16, 9.603, 44.201],
    [17, 42.573, 28.958], [18, 57.209, 28.958],
    [19, 42.460, 42.917], [20, 56.963, 42.917],
    [21, 71.258, 23.993], [22, 71.258, 35.590], [23, 71.258, 48.021],
    [24, 41.358, 89.861], [25, 56.060, 90.556],
    [26, 71.026, 90.903], [27, 86.060, 91.111],
  ]),
  area('terrace', 'Терраса', 2200, 610, 620, [
    [1, 5.909, 19.205], [2, 11.455, 55.584], [3, 14.363, 18.240],
    [4, 21.766, 69.029], [5, 23.894, 14.077], [6, 33.221, 75.072],
    [7, 32.621, 14.077], [8, 46.039, 75.072], [9, 65.221, 81.642],
    [10, 66.712, 43.913], [11, 78.039, 83.118],
    [12, 79.167, 43.913], [13, 91.584, 64.228],
  ]),
  // No balcony SVG was supplied. These positions follow the numbered PNG.
  area('balcony', 'Балкон', 615, 1185, 320, [
    [1, 81.300, 5.820], [2, 50.570, 24.560], [3, 81.950, 21.860],
    [4, 15.610, 40.760], [5, 48.780, 40.840], [6, 78.860, 45.150],
    [7, 48.460, 62.110], [8, 78.540, 61.270], [9, 46.670, 86.160],
  ]),
  area('vip', 'VIP', 1900, 920, 440, [
    [28, 35.961, 35.217], [29, 79.013, 72.527],
  ]),
];
