"use client";

const DEFAULT_SECTION_SELECTOR = "[data-pdf-section]";
const DEFAULT_MARGIN_MM = 8;
const DEFAULT_SECTION_GAP_MM = 4;
const MAX_CAPTURE_AREA = 16_000_000;
const CAPTURE_PADDING_PX = 16;
const SLICE_OVERLAP_PX = 2;
const PAGE_BREAK_TOLERANCE_MM = 1;
const EXCLUDED_SELECTORS = ["[data-pdf-exclude]", "[data-html2canvas-ignore]"];

export interface ExportScreenElementToPdfOptions {
  filename: string;
  sectionSelector?: string;
  marginMm?: number;
  sectionGapMm?: number;
}

interface CaptureDimensions {
  width: number;
  height: number;
}

function safeFilename(filename: string): string {
  const normalized = filename.trim().replace(/[\\/:*?"<>|]+/g, "-");
  return normalized.toLowerCase().endsWith(".pdf")
    ? normalized
    : `${normalized || "analysis-export"}.pdf`;
}

function getCaptureSections(root: HTMLElement, selector: string): HTMLElement[] {
  const sections = Array.from(root.querySelectorAll<HTMLElement>(selector)).filter(
    (section) => section.offsetParent !== null || section === root
  );
  return sections.length > 0 ? sections : [root];
}

function waitForAnimationFrame(): Promise<void> {
  return new Promise((resolve) => requestAnimationFrame(() => resolve()));
}

async function waitForCaptureReady(): Promise<void> {
  if ("fonts" in document) {
    await document.fonts.ready;
  }
  await waitForAnimationFrame();
  await waitForAnimationFrame();
}

function parseRgbColor(color: string): [number, number, number] {
  const match = color.match(
    /rgba?\(\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*[, ]\s*(\d{1,3})/i
  );
  if (!match) {
    return [255, 255, 255];
  }

  return [
    Number.parseInt(match[1] ?? "255", 10),
    Number.parseInt(match[2] ?? "255", 10),
    Number.parseInt(match[3] ?? "255", 10),
  ];
}

function isExcludedFromCapture(root: HTMLElement, node: Element): boolean {
  return EXCLUDED_SELECTORS.some((selector) => {
    const nearestExcluded = node.closest(selector);
    return nearestExcluded !== null && root.contains(nearestExcluded);
  });
}

function createCaptureFilter(root: HTMLElement) {
  return (node: Node) => {
    if (node === root) return true;
    if (!(node instanceof Element)) return true;

    return !isExcludedFromCapture(root, node);
  };
}

function getCaptureDimensions(root: HTMLElement): CaptureDimensions {
  const rootRect = root.getBoundingClientRect();
  let maxRight = rootRect.right;
  let maxBottom = rootRect.bottom;

  for (const node of Array.from(root.querySelectorAll("*"))) {
    if (!(node instanceof Element) || isExcludedFromCapture(root, node)) {
      continue;
    }

    const style = getComputedStyle(node);
    if (style.display === "none" || style.visibility === "hidden") {
      continue;
    }

    const rect = node.getBoundingClientRect();
    if (rect.width === 0 && rect.height === 0) {
      continue;
    }

    maxRight = Math.max(maxRight, rect.right);
    maxBottom = Math.max(maxBottom, rect.bottom);
  }

  return {
    width: Math.max(
      Math.ceil(maxRight - rootRect.left + CAPTURE_PADDING_PX),
      root.scrollWidth,
      root.clientWidth,
      1
    ),
    height: Math.max(
      Math.ceil(maxBottom - rootRect.top + CAPTURE_PADDING_PX),
      root.scrollHeight,
      root.clientHeight,
      1
    ),
  };
}

function getSafePixelRatio(
  dimensions: CaptureDimensions,
  requestedRatio: number
): number {
  const { width, height } = dimensions;
  const areaAtRequestedRatio = width * height * requestedRatio * requestedRatio;
  if (areaAtRequestedRatio <= MAX_CAPTURE_AREA) {
    return requestedRatio;
  }

  return Math.max(1, Math.sqrt(MAX_CAPTURE_AREA / (width * height)));
}

function createSliceCanvas(
  sourceCanvas: HTMLCanvasElement,
  offsetY: number,
  sliceHeight: number
): HTMLCanvasElement {
  const sliceCanvas = document.createElement("canvas");
  sliceCanvas.width = sourceCanvas.width;
  sliceCanvas.height = sliceHeight;

  const ctx = sliceCanvas.getContext("2d");
  if (!ctx) {
    throw new Error("Could not prepare the PDF export canvas.");
  }

  ctx.drawImage(
    sourceCanvas,
    0,
    offsetY,
    sourceCanvas.width,
    sliceHeight,
    0,
    0,
    sourceCanvas.width,
    sliceHeight
  );

  return sliceCanvas;
}

export async function exportScreenElementToPdf(
  root: HTMLElement,
  options: ExportScreenElementToPdfOptions
): Promise<void> {
  if (!root.isConnected) {
    throw new Error("The export target is no longer available.");
  }

  await waitForCaptureReady();

  const [{ jsPDF }, htmlToImage] = await Promise.all([
    import("jspdf"),
    import("html-to-image"),
  ]);

  const pdf = new jsPDF({
    orientation: "portrait",
    unit: "mm",
    format: "a4",
    compress: true,
  });

  const pageWidth = pdf.internal.pageSize.getWidth();
  const pageHeight = pdf.internal.pageSize.getHeight();
  const marginMm = options.marginMm ?? DEFAULT_MARGIN_MM;
  const sectionGapMm = options.sectionGapMm ?? DEFAULT_SECTION_GAP_MM;
  const usableWidthMm = pageWidth - marginMm * 2;
  const usableHeightMm = pageHeight - marginMm * 2;
  const pageBackgroundColor = parseRgbColor(
    getComputedStyle(document.body).backgroundColor
  );

  const paintPageBackground = () => {
    pdf.setFillColor(...pageBackgroundColor);
    pdf.rect(0, 0, pageWidth, pageHeight, "F");
  };

  const addNewPage = () => {
    pdf.addPage();
    paintPageBackground();
  };

  paintPageBackground();

  let currentY = marginMm;
  const sections = getCaptureSections(
    root,
    options.sectionSelector ?? DEFAULT_SECTION_SELECTOR
  );

  for (let index = 0; index < sections.length; index += 1) {
    const section = sections[index];
    const dimensions = getCaptureDimensions(section);
    const pixelRatio = getSafePixelRatio(dimensions, 2);
    const canvas = await htmlToImage.toCanvas(section, {
      cacheBust: true,
      pixelRatio,
      filter: createCaptureFilter(section),
      width: dimensions.width,
      height: dimensions.height,
      canvasWidth: dimensions.width,
      canvasHeight: dimensions.height,
    });

    const sectionHeightMm = (canvas.height * usableWidthMm) / canvas.width;
    const remainingHeightMm = pageHeight - currentY - marginMm;

    if (
      sectionHeightMm <= usableHeightMm &&
      sectionHeightMm > remainingHeightMm - PAGE_BREAK_TOLERANCE_MM &&
      currentY > marginMm
    ) {
      addNewPage();
      currentY = marginMm;
    }

    if (sectionHeightMm <= usableHeightMm) {
      pdf.addImage(
        canvas.toDataURL("image/png"),
        "PNG",
        marginMm,
        currentY,
        usableWidthMm,
        sectionHeightMm,
        undefined,
        "FAST"
      );
      currentY += sectionHeightMm;
    } else {
      const sliceHeightPx = Math.max(
        1,
        Math.floor((usableHeightMm * canvas.width) / usableWidthMm)
      );

      let offsetY = 0;
      while (offsetY < canvas.height) {
        if (offsetY > 0 || currentY > marginMm) {
          addNewPage();
          currentY = marginMm;
        }

        const remainingSlicePx = canvas.height - offsetY;
        const currentSliceHeightPx = Math.min(sliceHeightPx, remainingSlicePx);
        const sliceCanvas = createSliceCanvas(
          canvas,
          offsetY,
          currentSliceHeightPx
        );
        const sliceHeightMm =
          (sliceCanvas.height * usableWidthMm) / sliceCanvas.width;

        pdf.addImage(
          sliceCanvas.toDataURL("image/png"),
          "PNG",
          marginMm,
          currentY,
          usableWidthMm,
          sliceHeightMm,
          undefined,
          "FAST"
        );

        if (offsetY + currentSliceHeightPx >= canvas.height) {
          offsetY = canvas.height;
        } else {
          offsetY += Math.max(1, currentSliceHeightPx - SLICE_OVERLAP_PX);
        }
        currentY += sliceHeightMm;
      }
    }

    if (index < sections.length - 1) {
      currentY += sectionGapMm;
    }
  }

  pdf.save(safeFilename(options.filename));
}
