// UI language: Korean (default) or English. The choice is kept in localStorage ('nx.lang'); changing it reloads the page — the engine
// holds the drawing and the undo history, so nothing is lost (the view is fitted again).
// Static UI text is written as L(english, korean). Command-line prompts and history lines are produced in English (AutoCAD wording, and
// what the self-test reads) and translated phrase by phrase by xl() when the language is Korean.
export type Lang = 'ko' | 'en';
function load(): Lang { try { const v = localStorage.getItem('nx.lang'); if (v === 'en' || v === 'ko') return v; } catch { } return 'ko'; }
let cur: Lang = load();
try { document.documentElement.lang = cur; } catch { }
export function lang(): Lang { return cur; }
export function L(en: string, ko: string): string { return cur === 'ko' ? ko : en; }
/** Store the language and reload the page with it. */
export function setLang(l: Lang) { if (l === cur) return; try { localStorage.setItem('nx.lang', l); } catch { } location.reload(); }

// English phrase → Korean. Longer phrases are tried first, so a phrase may contain a shorter one.
const PHRASES: [string, string][] = [
  ['Select objects (click / window / crossing), Enter to continue:', '객체 선택 (클릭 / 창 / 교차), Enter = 계속:'],
  ['Select cutting edges (click / window / crossing), Enter when done (none = every object):', '자를 기준 객체 선택 (클릭 / 창 / 교차), 끝나면 Enter (선택 없음 = 모든 객체):'],
  ['Select boundary edges (click / window / crossing), Enter when done (none = every object):', '경계 객체 선택 (클릭 / 창 / 교차), 끝나면 Enter (선택 없음 = 모든 객체):'],
  ['Select object to trim (Shift+click = extend, drag = fence, T = choose cutting edges', '자를 부분 선택 (Shift+클릭 = 연장, 드래그 = 울타리, T = 기준 객체 지정'],
  ['Select object to extend (Shift+click = trim, drag = fence, T = choose boundary edges', '늘릴 끝 선택 (Shift+클릭 = 자르기, 드래그 = 울타리, T = 경계 객체 지정'],
  [', Enter = end):', ', Enter = 끝):'], [' chosen]', '개 지정]'],
  ['Every visible object is a cutting edge — pick the part to extend', '보이는 모든 객체가 경계입니다 — 늘릴 끝을 클릭하세요'],
  ['Every visible object is a cutting edge — pick the part to trim', '보이는 모든 객체가 자르는 기준입니다 — 지울 부분을 클릭하세요'],
  ['edge(s) chosen', '개 기준 객체 지정'], ['every object is an edge', '모든 객체가 기준'], ['edge(s) — Enter to continue', '개 기준 객체 — Enter = 계속'],
  ['object(s)', '개 객체'],
  ['Specify new point (drag or click, Esc = cancel):', '새 위치 지정 (끌거나 클릭, Esc = 취소):'],
  ['[Quick]', '[빠른 모드]'], ['[Standard]', '[표준 모드]'], ['Select object to trim, Shift = extend', '자를 부분 선택, Shift = 연장'], ['Select object to extend, Shift = trim', '늘릴 끝 선택, Shift = 자르기'], [' edges]', '개 기준]'],
  ['[T edges / F fence / C crossing / O mode / R erase / U undo], Enter = end:', '[T 기준 지정 / F 울타리 / C 교차 / O 모드 / R 지우기 / U 취소], Enter = 끝:'],
  ['Select objects to erase (click / fence), Enter = back:', '지울 객체 선택 (클릭 / 울타리), Enter = 돌아가기:'],
  ['Mode = Quick: every object is an edge; an object that nothing cuts is erased', '모드 = 빠른 모드: 모든 객체가 기준, 아무것과도 만나지 않는 객체는 지워집니다'],
  ['Mode = Standard: choose the edges first (Enter = every object)', '모드 = 표준 모드: 기준 객체를 먼저 선택 (Enter = 모든 객체)'],
  ['Fence: click two points on empty space, or drag across the objects', '울타리: 빈 곳에서 두 점을 클릭하거나, 객체 위를 끌어서 지나가세요'], ['Crossing: click two corners on empty space', '교차: 빈 곳에서 두 모서리를 클릭하세요'],
  ['the object does not cross a cutting edge', '객체가 자르는 기준과 만나지 않습니다'], ['a closed object needs two cutting points', '닫힌 객체는 만나는 점이 두 개 있어야 합니다'],
  ['no boundary edge in that direction', '그 방향에 경계 객체가 없습니다'], ['a closed object cannot be extended', '닫힌 객체는 연장할 수 없습니다'],
  ['the object is on a locked layer', '잠긴 레이어의 객체입니다'], ['nothing would remain', '남는 부분이 없습니다'], ['zero-length object', '길이가 0인 객체'], ['zero-length end segment', '끝 구간의 길이가 0'],
  ['cannot be trimmed (line, arc, circle, polyline)', '은(는) 자를 수 없습니다 (선·호·원·폴리선만)'], ['cannot be extended (line, arc, circle, polyline)', '은(는) 연장할 수 없습니다 (선·호·원·폴리선만)'],
  ['Specify first extension line origin:', '첫 번째 치수보조선 원점 지정:'], ['Specify second extension line origin:', '두 번째 치수보조선 원점 지정:'], ['Specify dimension line location:', '치수선 위치 지정:'],
  ['Specify first point of mirror line:', '대칭선의 첫 번째 점 지정:'], ['Specify second point of mirror line:', '대칭선의 두 번째 점 지정:'], ['Erase source objects? [Yes/No] <N>:', '원본 객체를 지울까요? [예(Y)/아니오(N)] <N>:'],
  ['Specify second point for another copy (Enter to end):', '다음 복사 위치 지정 (Enter = 끝):'],
  ['Specify rotation angle (value or point):', '회전 각도 지정 (값 또는 점):'], ['Specify rotation angle of text', '문자 회전 각도 지정'],
  ['Specify next point (Enter = end, C = close):', '다음 점 지정 (Enter = 끝, C = 닫기):'], ['Specify next point (Enter to end):', '다음 점 지정 (Enter = 끝):'],
  ['Specify radius (point or value)', '반지름 지정 (점 또는 값)'], ['Specify other corner point (or @dx,dy):', '반대 모서리 지정 (또는 @dx,dy):'],
  ['Specify first corner point:', '첫 번째 모서리 지정:'], ['Specify first corner:', '첫 번째 모서리 지정:'], ['Specify opposite corner (sets the width):', '반대 모서리 지정 (폭이 정해짐):'],
  ['Specify second point of arc:', '호의 두 번째 점 지정:'], ['Specify end point of arc:', '호의 끝점 지정:'],
  ['Specify other endpoint of axis:', '축의 다른 끝점 지정:'], ['Specify axis endpoint:', '축의 끝점 지정:'], ['Specify distance to other axis (point or value):', '다른 축까지의 거리 지정 (점 또는 값):'],
  ['Specify center of polygon:', '다각형의 중심 지정:'], ['Enter number of sides', '변의 수 입력'], [' sides', '변'],
  ['Inscribed: specify radius (point or value), I = inscribed, C = circumscribed:', '내접: 반지름 지정 (점 또는 값), I = 내접, C = 외접:'],
  ['Circumscribed: specify radius (point or value), I = inscribed, C = circumscribed:', '외접: 반지름 지정 (점 또는 값), I = 내접, C = 외접:'],
  ['Specify start point of text (height ', '문자 시작점 지정 (높이 '], ['Specify height', '높이 지정'],
  ['Enter text, line 1 (Enter on an empty line ends):', '문자 입력, 1번째 줄 (빈 줄에서 Enter = 끝):'], ['Enter text (Enter on an empty line ends):', '문자 입력 (빈 줄에서 Enter = 끝):'],
  ['next line (Enter on an empty line ends):', '다음 줄 (빈 줄에서 Enter = 끝):'], ['(Enter on an empty line ends):', '(빈 줄에서 Enter = 끝):'],
  ['Select object to offset (Enter to end):', '간격띄울 객체 선택 (Enter = 끝):'], ['Select object to offset:', '간격띄울 객체 선택:'], ['Specify offset distance', '간격띄우기 거리 지정'], ['Specify point on side to offset:', '간격띄울 쪽의 점 지정:'],
  ['Specify center point:', '중심점 지정:'], ['Specify start point:', '시작점 지정:'], ['Specify first point:', '첫 번째 점 지정:'], ['Specify second point:', '두 번째 점 지정:'], ['Specify base point:', '기준점 지정:'],
  ['Specify a point:', '점 지정:'], ['Specify point:', '점 지정:'],
  ['[All/Extents/Window/Previous] <Extents>:', '[전체(A)/범위(E)/창(W)/이전(P)] <범위>:'], ['first corner:', '첫 번째 구석:'], ['opposite corner:', '반대 구석:'],
  ['drag, Esc to end', '드래그, Esc = 끝'], ['Command:', '명령:'],
  ['selected — Enter to continue', '개 선택됨 — Enter = 계속'], ['STRETCH needs a crossing window selection', 'STRETCH는 교차 창으로 선택해야 합니다'],
  ['Select a TEXT or MTEXT first (or double-click it)', '먼저 TEXT 또는 MTEXT를 선택하세요 (또는 더블클릭)'],
  ['the corners must differ in X and Y', '두 모서리의 X와 Y가 달라야 합니다'], ['the three points are on one line', '세 점이 한 직선 위에 있습니다'], ['number of sides 3 … 1024', '변의 수는 3 … 1024'],
  ['no previous view', '이전 보기가 없습니다'], ['geometry and render buffers rebuilt', '도형과 렌더 버퍼를 다시 만들었습니다'], ['(wheel double-click)', '(휠 더블클릭)'],
  ['(source erased)', '(원본 삭제)'], ['(closed)', '(닫힘)'], [' vertices', ' 정점'], [' line(s)', '줄'],
  ['Unknown command:', '알 수 없는 명령:'], ['rail focused (type a name + Enter = new layer)', '레일로 이동 (이름 입력 + Enter = 새 레이어)'], ['rail focused', '레일로 이동'],
  ['Right click = time-sensitive', '우클릭 = 시간 감지'], ['Right click = menu', '우클릭 = 메뉴'], ['Right click = Enter (confirm / repeat)', '우클릭 = Enter (확정 / 반복)'], ['ms Enter, longer = menu)', 'ms = Enter, 길게 = 메뉴)'],
  ['<Object Snap Tracking on>', '<객체 스냅 추적 켜짐>'], ['<Object Snap Tracking off>', '<객체 스냅 추적 꺼짐>'],
  ['<Ortho on>', '<직교 켜짐>'], ['<Ortho off>', '<직교 꺼짐>'], ['<Polar on>', '<극좌표 추적 켜짐>'], ['<Polar off>', '<극좌표 추적 꺼짐>'],
  ['<Grid on>', '<그리드 켜짐>'], ['<Grid off>', '<그리드 꺼짐>'], ['<Snap on>', '<그리드 스냅 켜짐>'], ['<Snap off>', '<그리드 스냅 꺼짐>'], ['<Osnap on>', '<객체 스냅 켜짐>'], ['<Osnap off>', '<객체 스냅 꺼짐>'],
  ['Distance = ', '거리 = '], ['Angle in XY = ', 'XY 각도 = '], ['Delta X = ', 'ΔX = '], ['Delta Y = ', 'ΔY = '],
  ['horizontal', '수평'], ['vertical', '수직'], ['inscribed', '내접'], ['circumscribed', '외접'], ['TEXT edited:', 'TEXT 수정:'],
  ['Loaded ', '불러옴: '], [' entities (', '개 객체 ('], [' ms page, first frame ', ' ms 페이지, 첫 프레임 '],
];
const SORTED = PHRASES.slice().sort((a, b) => b[0].length - a[0].length);
/** Translate a prompt / history line to the UI language (English text is returned as it is). */
export function xl(text: string): string {
  if (cur !== 'ko' || !text) return text;
  let s = text; for (const [en, ko] of SORTED) if (s.includes(en)) s = s.split(en).join(ko);
  return s;
}
