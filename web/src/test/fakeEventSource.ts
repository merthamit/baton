import { vi } from 'vitest';

/** A stand-in for the browser's EventSource (jsdom has none): tests open it and send named events by hand. */
export class FakeEventSource {
  static instances: FakeEventSource[] = [];
  readonly url: string;
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;
  /** 0 connecting, 1 open, 2 closed (the browser gave up reconnecting). */
  readyState = 0;
  private listeners = new Map<string, ((e: MessageEvent) => void)[]>();

  constructor(url: string) {
    this.url = url;
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, listener: (e: MessageEvent) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  close() {
    this.closed = true;
  }

  emit(type: string, data: unknown) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(new MessageEvent(type, { data: JSON.stringify(data) }));
    }
  }

  static install() {
    FakeEventSource.instances = [];
    vi.stubGlobal('EventSource', FakeEventSource);
  }

  static last(): FakeEventSource {
    const last = FakeEventSource.instances.at(-1);
    if (!last) {
      throw new Error('No EventSource was opened.');
    }
    return last;
  }
}
