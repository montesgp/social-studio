"use client";

export interface ExportElementToPdfOptions {
  filename: string;
}

const STYLE_ID = "inc-pdf-print-style";
const ANCESTOR_CLASS = "inc-pdf-ancestor";

function safeFilename(filename: string): string {
  const normalized = filename.trim().replace(/[\\/:*?"<>|]+/g, "-");
  return normalized.toLowerCase().endsWith(".pdf")
    ? normalized
    : `${normalized || "analysis-export"}.pdf`;
}

/**
 * Triggers the browser's native print-to-PDF dialog for the given element.
 *
 * Instead of `body { visibility: hidden }` (which keeps invisible elements in
 * the layout flow and creates blank whitespace above the target), this function
 * walks the ancestor chain from `element` up to `<body>` and hides every
 * sibling element at each level with `display: none`. Ancestors receive a
 * temporary class that zeroes out their padding-top/margin-top in print so the
 * content starts at the top of the first page.
 */
export async function exportElementToPdf(
  element: HTMLElement,
  options: ExportElementToPdfOptions
): Promise<void> {
  const prevTitle = document.title;
  document.title = safeFilename(options.filename);

  // ── Hide siblings along the ancestor chain ──────────────────────────────────
  const hiddenSiblings: { el: HTMLElement; prevDisplay: string }[] = [];

  let cur: HTMLElement = element;
  while (cur !== document.body && cur.parentElement) {
    const parent = cur.parentElement as HTMLElement;
    parent.classList.add(ANCESTOR_CLASS);
    Array.from(parent.children).forEach((child) => {
      if (child !== cur && child instanceof HTMLElement) {
        hiddenSiblings.push({ el: child, prevDisplay: child.style.display });
        child.style.display = "none";
      }
    });
    cur = parent;
  }

  // ── Inject print styles ──────────────────────────────────────────────────────
  let styleEl = document.getElementById(STYLE_ID) as HTMLStyleElement | null;
  if (!styleEl) {
    styleEl = document.createElement("style");
    styleEl.id = STYLE_ID;
    document.head.appendChild(styleEl);
  }

  styleEl.textContent = `
    @media print {
      body { padding: 0 !important; margin: 0 !important; }
      .${ANCESTOR_CLASS} { padding-top: 0 !important; margin-top: 0 !important; }
      [data-pdf-exclude],
      [data-html2canvas-ignore] { display: none !important; }
      [data-pdf-main-grid] { display: block !important; }
      * { -webkit-print-color-adjust: exact !important; print-color-adjust: exact !important; }
      @page { size: A4 portrait; margin: 1.5cm; }
    }
  `;

  // Two rAF cycles ensure the browser has re-painted before the print dialog opens.
  await new Promise<void>((resolve) =>
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  );

  return new Promise<void>((resolve) => {
    function cleanup() {
      hiddenSiblings.forEach(({ el, prevDisplay }) => {
        el.style.display = prevDisplay;
      });
      document.querySelectorAll(`.${ANCESTOR_CLASS}`).forEach((el) =>
        el.classList.remove(ANCESTOR_CLASS)
      );
      if (styleEl) styleEl.textContent = "";
      document.title = prevTitle;
      resolve();
    }

    const afterPrint = () => {
      window.removeEventListener("afterprint", afterPrint);
      cleanup();
    };
    window.addEventListener("afterprint", afterPrint);

    // Fallback: resolve after 60 s if afterprint never fires (e.g., some browsers/OS).
    const fallback = setTimeout(() => {
      window.removeEventListener("afterprint", afterPrint);
      cleanup();
    }, 60_000);
    window.addEventListener("afterprint", () => clearTimeout(fallback), { once: true });

    window.print();
  });
}
