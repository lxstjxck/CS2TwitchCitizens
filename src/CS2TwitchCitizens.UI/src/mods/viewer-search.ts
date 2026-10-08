export function filterViewers<T extends { login: string; displayName: string }>(viewers: T[], query: string): T[] {
  const needle = query.trim().toLocaleLowerCase();
  if (!needle) return viewers;
  return viewers.filter((viewer) => viewer.login.toLocaleLowerCase().includes(needle) ||
    viewer.displayName.toLocaleLowerCase().includes(needle));
}
