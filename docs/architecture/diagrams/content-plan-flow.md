# Content Plan — flujo conceptual

Cómo se llega a generar un **Content Package** a partir del contexto del cliente.
Diseño asociado: [`ADR 0002 — client-content-plan-model`](../adr/0002-client-content-plan-model.md).

> Fuente editable: [`content-plan-flow.excalidraw`](./content-plan-flow.excalidraw) (abrir en https://excalidraw.com).
> El Mermaid de abajo es la versión que renderiza directo en GitHub y es la fuente versionada de referencia.

```mermaid
flowchart TD
    subgraph enduring["Contexto perdurable — nivel Cliente"]
        direction TB
        BB["Brand Brain<br/><i>(= Brand Manual)</i>"]
        GS["General Strategy"]
    end

    subgraph plan["Content Plan · [Period — hoy mensual]"]
        direction TB
        OBJ["Monthly Objective<br/><i>{Manual | AI}</i>"]
        CS["AI Content Strategy<br/><i>deriva de Brand Brain + General Strategy + Objective</i>"]
        CAL["Content Calendar — slots<br/><i>Lun: Reel · Mié: Carrusel · Vie: Caso de éxito · Dom: Historia</i>"]
        OBJ --> CS --> CAL
    end

    BB --> GS
    GS --> OBJ
    CAL -->|snapshot del contexto vigente| DEC{"¿Generación<br/>simple o masiva?"}
    DEC -->|"simple → 1 slot"| PKG
    DEC -->|"masiva → todos los slots"| PKG["Content Package<br/><i>inmutable · no se cura para atrás</i>"]

    classDef enduringNode fill:#a5d8ff,stroke:#4a9eed,color:#0b2545;
    classDef planNode fill:#d0bfff,stroke:#8b5cf6,color:#2e1065;
    classDef objNode fill:#fff3bf,stroke:#f59e0b,color:#7c2d12;
    classDef pkgNode fill:#b2f2bb,stroke:#22c55e,color:#14532d;
    class BB,GS enduringNode;
    class CS,CAL planNode;
    class OBJ objNode;
    class PKG pkgNode;
```

## Leyenda

- **Brand Brain / General Strategy** — contexto perdurable a nivel cliente; se define al inicio y casi no cambia.
- **Content Plan** — agregado por período (hoy mensual; el período es un Value Object, no está atado a la cadencia). Contiene:
  - **Objective** — objetivo + KPIs del período (manual o IA).
  - **Content Strategy** — estrategia táctica del período, generada/refinada por IA.
  - **Content Calendar** — slots `día → formato`; cada slot es una unidad de generación.
- Cada nodo lleva **`Provenance` `{ Source: Manual | AI, GeneratedAt, Status }`** como traza de auditoría (no bloqueante).
- **Generación**: *simple* = 1 slot; *masiva* = todos los slots. Toma un **snapshot** del contexto vigente y produce el **Content Package**, que es **inmutable** (no se cura para atrás).
