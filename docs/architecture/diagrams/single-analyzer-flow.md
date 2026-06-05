# Single Analyzer — happy path (Fase A)

Cómo se pasa de un URL de contenido a un `package.zip` descargable, con la rama de exportación a Canva marcada como placeholder de Fase B.
Diseño asociado: [`design.md`](../../../openspec/changes/single-analyzer-fase-a/design.md).

> Fuente editable: [`single-analyzer-flow.excalidraw`](./single-analyzer-flow.excalidraw) (abrir en https://excalidraw.com).
> El Mermaid de abajo es la versión que renderiza directo en GitHub y es la fuente versionada de referencia.

```mermaid
flowchart TD
    INPUT["Submit URL + clientId"]

    subgraph analysis["SingleContentAnalysis — pipeline de estados"]
        direction TB
        ANALYZE["analyzeSingleContent (202)"]
        POLL["Poll: Requested → ResolvingContent\n→ ContentResolved → Analyzing → Analyzed"]
        ANALYZE --> POLL
    end

    subgraph generation["ContentGenerationJob"]
        direction TB
        REQPKG["requestPackage (202)"]
        JOBPOLL["ContentGenerationJob — poll"]
        REQPKG --> JOBPOLL
    end

    subgraph orchestrator["ContentPackageOrchestrator — per-format generation"]
        direction TB
        BRAND["BrandSnapshot load"]
        GEN["per-format generate\n(AI → composer)"]
        VISUAL["VisualCompositionService"]
        RENDER["IContentRenderer\nPNG | .md | .txt"]
        BRAND --> GEN --> VISUAL --> RENDER
    end

    STORAGE["assets stored per client\n(folder = client display name, sanitized)"]
    ZIP["GeneratedPackageBuilder — frozen ZIP"]

    INPUT --> ANALYZE
    POLL -->|"Analyzed"| REQPKG
    JOBPOLL -->|"Complete"| BRAND
    RENDER --> STORAGE --> ZIP

    ZIP --> DEC{"Download vs\nExport to Canva?"}

    DEC -->|"Download"| PKG["Download package.zip"]
    DEC -.->|"Fase B"| CANVA["Export to Canva\n(Fase B — placeholder)"]

    classDef inputNode fill:#a5d8ff,stroke:#4a9eed,color:#0b2545;
    classDef pipelineNode fill:#d0bfff,stroke:#8b5cf6,color:#2e1065;
    classDef decisionNode fill:#fff3bf,stroke:#f59e0b,color:#7c2d12;
    classDef terminalOk fill:#b2f2bb,stroke:#22c55e,color:#14532d;
    classDef phaseBNode fill:#fff3bf,stroke:#f59e0b,color:#7c2d12,stroke-dasharray:6 4,opacity:0.7;

    class INPUT inputNode;
    class ANALYZE,POLL,REQPKG,JOBPOLL,BRAND,GEN,VISUAL,RENDER,STORAGE,ZIP pipelineNode;
    class DEC decisionNode;
    class PKG terminalOk;
    class CANVA phaseBNode;
```

> **Nota:** Ambas ramas (`Download` y `Export to Canva`) deben producir formatos compatibles con Canva. La integración directa con Canva está fuera del alcance de Fase A.

## Leyenda

- **Entrada (azul)** — el nodo `Submit URL + clientId` es el punto de entrada del pipeline. `clientId` es opcional; sin él, no se puede generar el paquete de contenido.
- **Pipeline (morado)** — etapas de procesamiento:
  - `analyzeSingleContent (202)` — encola el job de análisis y devuelve inmediatamente.
  - Poll de estados `Requested → ResolvingContent → ContentResolved → Analyzing → Analyzed` — el frontend poll hasta `Analyzed`.
  - `requestPackage (202)` — encola el `ContentGenerationJob` y devuelve inmediatamente.
  - `ContentPackageOrchestrator` — orquesta la generación por formato: carga el `BrandSnapshot`, invoca el AI composer por formato, pasa el resultado a `VisualCompositionService`, renderiza via `IContentRenderer`.
  - `IContentRenderer` — branch por `OutputKind`: `Text` → `.txt`, `Markdown` → `.md` (`reel-script.md`, `text/markdown; charset=utf-8`), default → raster PNG.
  - `assets stored per client` — assets almacenados con carpeta = nombre display del cliente (sanitizado, no slugificado).
  - `GeneratedPackageBuilder` — construye el ZIP inmutable (`frozen`).
- **Decisión (ámbar)** — `Download vs Export to Canva?` — el nodo de decisión terminal.
- **Terminal verde** — `Download package.zip` — ruta disponible en Fase A.
- **Placeholder Fase B (ámbar punteado)** — `Export to Canva` — fuera del alcance de Fase A; reservado para integración directa con la API de Canva.

## Notas de implementación

| Aspecto | Decisión |
|---|---|
| `reel-script` output | `OutputKind = Markdown` → archivo `reel-script.md`, MIME `text/markdown; charset=utf-8`, timestamps en negrita `**0:00**`, emojis preservados verbatim |
| Folder de cliente (ZIP) | `NormalizeFolderName(client.Name)` — sanitize-only: elimina 9 chars ilegales + C0/0x7F, trim leading/trailing dot y espacio, guard de device names (NUL→NUL\_). Sin slugificación, sin ToLowerInvariant |
| Folder de cliente (Assemble) | `AssembleGeneratedPackageQueryHandler` resuelve el nombre via `ISocialClientRepository.GetByIdAsync` — mismo `NormalizeFolderName`, fallback a GUID si el cliente no existe |
| `IContentRenderer` branch order | `Text` → `.txt` first, luego `Markdown` → `.md`, default → raster PNG |
| Canva | Ambas ramas producen formatos Canva-compatibles; la integración directa es Fase B |
| Spec IDs cubiertos | RS-01, RS-02, RS-03 (Markdown reel-script), PKG-01, PKG-02 (display-name folders), DIAG-01 (este diagrama) |
