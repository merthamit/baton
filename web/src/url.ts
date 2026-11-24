// The board's view state lives in the URL (?at=…&bed=…) so a moment on the ward can be shared or bookmarked.

export interface UrlState {
  at?: string;
  bed?: string;
}

const withOffset = /(Z|[+-]\d{2}:\d{2})$/;

export function readUrlState(search: string): UrlState {
  const params = new URLSearchParams(search);
  const state: UrlState = {};
  const at = params.get('at');
  const bed = params.get('bed');
  if (at && withOffset.test(at)) {
    state.at = at;
  }
  if (bed) {
    state.bed = bed;
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
  return parts.length ? `?${parts.join('&')}` : '';
}
