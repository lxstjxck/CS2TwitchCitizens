export function filterViewers<T extends { login: string; displayName: string; citizenName?: string }>(viewers: T[], query: string): T[] {
  const needle = query.trim().toLowerCase();
  if (!needle) return viewers;
  return viewers.filter((viewer) => viewer.login.toLowerCase().includes(needle) ||
    viewer.displayName.toLowerCase().includes(needle) ||
    (viewer.citizenName ?? "").toLowerCase().includes(needle));
}
