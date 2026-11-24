// The board's view state lives in the URL (?at=…&bed=…) so a moment on the ward can be shared or bookmarked.

export interface UrlState {
  at?: string;
  bed?: string;
  /** The handover view; the board is the default and is not written. */
  view?: 'handover';
  /** A running private shift (week 10) and the role this tab plays in it; nurse is the default. */
  shift?: string;
  role?: 'doctor';
}

const withOffset = /(Z|[+-]\d{2}:\d{2})$/;

export function readUrlState(search: string): UrlState {
  const params = new URLSearchParams(search);
  const state: UrlState = {};
  // URLSearchParams turns an unencoded '+' into a space: put the offset's sign back.
  const at = params.get('at')?.replace(/ (\d{2}:\d{2})$/, '+$1') ?? null;
  const bed = params.get('bed');
  if (at && withOffset.test(at)) {
    state.at = at;
  }
  if (bed) {
    state.bed = bed;
  }
  if (params.get('view') === 'handover') {
    state.view = 'handover';
  }
  const shift = params.get('shift');
  if (shift && /^[0-9a-f]{32}$/.test(shift)) {
    state.shift = shift;
  }
  if (state.shift && params.get('role') === 'doctor') {
    state.role = 'doctor';
  }
  return state;
}

export function writeUrlState(state: UrlState): string {
  const parts: string[] = [];
  if (state.at) {
    parts.push(`at=${encodeURIComponent(state.at)}`);
  }
  if (state.bed) {
    parts.push(`bed=${encodeURIComponent(state.bed)}`);
  }
  if (state.view === 'handover') {
    parts.push('view=handover');
  }
  if (state.shift) {
    parts.push(`shift=${state.shift}`);
    if (state.role === 'doctor') {
      parts.push('role=doctor');
    }
  }
  return parts.length ? `?${parts.join('&')}` : '';
}
