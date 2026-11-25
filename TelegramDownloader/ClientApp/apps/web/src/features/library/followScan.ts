const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

let current = 0;

/**
 * Calls `onEnd` when no scan runs any more. A queued scan starts right after
 * the running one, so it takes two quiet checks. A newer call replaces it.
 */
export async function followScan(isRunning: () => Promise<boolean>, onEnd: () => void, intervalMs = 1000, maxChecks = 900): Promise<void> {
  const id = ++current;
  let quiet = 0;
  for (let i = 0; i < maxChecks && quiet < 2; i++) {
    await wait(intervalMs);
    if (id !== current) return;
    try {
      quiet = (await isRunning()) ? 0 : quiet + 1;
    } catch {
      // Offline or restarting: ask again
    }
  }
  if (id === current) onEnd();
}
