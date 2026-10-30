// WebView2 bridge: JSON commands with replies, event fan-out. Outside WebView2 (vite dev server) a mock answers with an empty state.
declare global { interface Window { chrome?: any; nx: any; nxProbe?: any; } }
export type Listener = (msg: any) => void;

class Bridge {
  private id = 0;
  private pending = new Map<number, { res: (v: any) => void; rej: (e: any) => void }>();
  private listeners = new Map<string, Listener[]>();
  readonly hosted: boolean;
  constructor() {
    this.hosted = !!(window.chrome && window.chrome.webview);
    if (this.hosted) window.chrome.webview.addEventListener('message', (e: any) => this.onMessage(typeof e.data === 'string' ? JSON.parse(e.data) : e.data));   // doc messages arrive as strings (PostWebMessageAsString: no JSON validation on the UI thread)
  }
  on(ev: string, fn: Listener) { const l = this.listeners.get(ev) || []; l.push(fn); this.listeners.set(ev, l); }
  private emit(ev: string, msg: any) { for (const fn of this.listeners.get(ev) || []) { try { fn(msg); } catch (err) { console.error(err); } } }
  private onMessage(msg: any) {
    if (!msg || typeof msg !== 'object') return;
    if (msg.ev === 'reply') { const p = this.pending.get(msg.id); if (!p) return; this.pending.delete(msg.id); if (msg.ok) p.res(msg.result); else p.rej(new Error(msg.result || 'command failed')); return; }
    this.emit(msg.ev, msg);
  }
  post(msg: any) { if (this.hosted) window.chrome.webview.postMessage(msg); }
  call<T = any>(cmd: string, args: Record<string, any> = {}): Promise<T> {
    if (!this.hosted) return Promise.resolve(mock(cmd, args) as T);
    const id = ++this.id;
    return new Promise<T>((res, rej) => { this.pending.set(id, { res, rej }); window.chrome.webview.postMessage({ id, cmd, ...args }); });
  }
  postFiles(files: FileList | File[]) {
    if (!this.hosted || !window.chrome.webview.postMessageWithAdditionalObjects) return;
    window.chrome.webview.postMessageWithAdditionalObjects({ cmd: 'files' }, Array.from(files as any as File[]));
  }
  log(text: string) { if (this.hosted) this.post({ cmd: 'log', text }); else console.log(text); }
}
function mock(cmd: string, _args: any) {
  if (cmd === 'state') return { app: { name: 'NexysDraft', versionText: 'dev' }, theme: 'light', status: { text: 'dev server — no engine', led: 'idle' }, file: null, layers: [], currentLayer: '0', selection: [], undo: { can: false, canRedo: false }, recent: [] };
  return null;
}
export const bridge = new Bridge();
