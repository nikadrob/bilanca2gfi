let onUnauthorized: (() => void) | null = null;

/** Registered by AuthProvider: called when the server answers 401. */
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

/** fetch() that reports an expired/missing session to the auth context. */
export async function apiFetch(input: RequestInfo, init?: RequestInit): Promise<Response> {
  const response = await fetch(input, init);
  if (response.status === 401) onUnauthorized?.();
  return response;
}
