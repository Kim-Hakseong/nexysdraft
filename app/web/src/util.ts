export const h = (html: string): HTMLElement => { const t = document.createElement('template'); t.innerHTML = html.trim(); return t.content.firstElementChild as HTMLElement; };
export const esc = (s: any) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
export const fmtInt = (n: number) => Math.round(n).toLocaleString('en-US');
export const fmtBytes = (b: number) => b >= 1048576 ? (b / 1048576).toFixed(1) + ' MB' : (b / 1024).toFixed(0) + ' KB';
export function fmtNum(v: number, dec = 4): string { if (!Number.isFinite(v)) return '—'; const a = Math.abs(v); if (a >= 1e9 || (a < 1e-4 && a > 0)) return v.toExponential(3); return v.toFixed(dec).replace(/\.?0+$/, '') || '0'; }
export const debounce = <T extends (...a: any[]) => void>(fn: T, ms: number) => { let t = 0; return (...a: Parameters<T>) => { clearTimeout(t); t = window.setTimeout(() => fn(...a), ms); }; };

// AutoCAD Color Index → RGB (standard 256 palette; 7 is white/black depending on background)
const ACI: number[] = [];
(function build() {
  const base = [[0, 0, 0], [255, 0, 0], [255, 255, 0], [0, 255, 0], [0, 255, 255], [0, 0, 255], [255, 0, 255], [255, 255, 255], [65, 65, 65], [128, 128, 128]];
  for (const c of base) ACI.push((c[0] << 16) | (c[1] << 8) | c[2]);
  const hues = [0, 15, 30, 45, 60, 75, 90, 105, 120, 135, 150, 165, 180, 195, 210, 225, 240, 255, 270, 285, 300, 315, 330, 345];
  const vals = [[1, 1], [0.65, 1], [0.5, 1], [0.3, 1], [1, 0.5], [0.65, 0.5], [0.5, 0.5], [0.3, 0.5], [1, 0.25], [0.65, 0.25]];
  for (const hue of hues) for (let k = 0; k < 10; k += 2) {
    for (const sat of [1, 0.5]) {
      const v = vals[k][0]; const s = sat; const c = hsv(hue, s, v); ACI.push(c);
    }
  }
  while (ACI.length < 250) ACI.push(0x808080);
  const greys = [0x333333, 0x505050, 0x696969, 0x828282, 0xBEBEBE, 0xFFFFFF];
  for (const g of greys) ACI.push(g);
})();
function hsv(hdeg: number, s: number, v: number): number {
  const c = v * s, x = c * (1 - Math.abs(((hdeg / 60) % 2) - 1)), m = v - c; let r = 0, g = 0, b = 0;
  if (hdeg < 60) [r, g, b] = [c, x, 0]; else if (hdeg < 120) [r, g, b] = [x, c, 0]; else if (hdeg < 180) [r, g, b] = [0, c, x]; else if (hdeg < 240) [r, g, b] = [0, x, c]; else if (hdeg < 300) [r, g, b] = [x, 0, c]; else [r, g, b] = [c, 0, x];
  return (Math.round((r + m) * 255) << 16) | (Math.round((g + m) * 255) << 8) | Math.round((b + m) * 255);
}
/** RGB int of an ACI index on a given canvas background ("dark" → 7 is white; "light" → 7 is black, very light colours darkened). */
export function aciRgb(aci: number, dark: boolean): number {
  aci = Math.abs(aci) | 0;
  if (aci === 7 || aci === 0 || aci === 256) return dark ? 0xFFFFFF : 0x17181A;
  let c = ACI[aci] ?? 0x808080;
  const r = c >> 16, g = (c >> 8) & 255, b = c & 255; const lum = 0.299 * r + 0.587 * g + 0.114 * b;
  if (!dark) { if (lum > 200) c = ((r * 0.55) << 16) | ((g * 0.55) << 8) | (b * 0.55 | 0); }
  // dark background: lightness floor — black-ish colours (ACI 8, 250–251, dark shades) are lifted toward grey so they stay visible (draft5 I2)
  else if (Math.max(r, g, b) < 110) { const k = 110 - Math.max(r, g, b); const f = (x: number) => Math.min(255, Math.round(x + k)); c = (f(r) << 16) | (f(g) << 8) | f(b); }
  return c;
}
export const rgbCss = (c: number) => `rgb(${c >> 16},${(c >> 8) & 255},${c & 255})`;
