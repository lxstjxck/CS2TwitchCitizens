/** Gameface lays out styles after React commits. Inspect the rendered box before acknowledging it. */
export function noticeIsVisible(node: HTMLElement, viewportWidth: number, viewportHeight: number,
  styleOf: (element: Element) => CSSStyleDeclaration): boolean {
  if (viewportWidth <= 0 || viewportHeight <= 0) return false;
  const box = node.getBoundingClientRect();
  if (![box.width, box.height, box.left, box.top, box.right, box.bottom].every(Number.isFinite) ||
    box.width <= 0 || box.height <= 0 || box.left < 0 || box.top < 0 ||
    box.right > viewportWidth || box.bottom > viewportHeight) return false;
  for (let current: Element | null = node; current; current = current.parentElement) {
    const style = styleOf(current);
    if (style.display === "none" || style.visibility === "hidden" || style.visibility === "collapse" ||
      Number.parseFloat(style.opacity) === 0) return false;
    if (current !== node && [style.overflow, style.overflowX, style.overflowY]
      .some(value => ["hidden", "clip", "auto", "scroll"].includes(value))) {
      const parent = current.getBoundingClientRect();
      if (box.left < parent.left || box.top < parent.top || box.right > parent.right ||
        box.bottom > parent.bottom) return false;
    }
  }
  return true;
}
