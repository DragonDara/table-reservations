/** Block a repeated tap before it can navigate the next rendered step. */
export function createNavigationGuard(now: () => number = () => performance.now()) {
  let lastNavigationAt = -Infinity;
  return () => {
    const timestamp = now();
    if (timestamp - lastNavigationAt < 350) return false;
    lastNavigationAt = timestamp;
    return true;
  };
}

export function isChoiceStepAnswered(step: string | undefined, categoryId: string, serviceCount: number, hasTime: boolean): boolean {
  if (step === "category") return Boolean(categoryId);
  if (step === "service") return serviceCount > 0;
  if (step === "time") return hasTime;
  return true; // Text/date fields retain their normal validation on submission.
}
