# Profile Analysis — flujo del pipeline (Mundo 2)

Cómo se pasa de una fuente Instagram a un `ProfileAnalysis` con análisis de IA, dashboard de métricas e imputación de campos al cliente.
Diseño asociado: [`ADR 0003 — profile-analysis-model`](../adr/0003-profile-analysis-model.md).

> Fuente editable: [`profile-analysis-flow.excalidraw`](./profile-analysis-flow.excalidraw) (abrir en https://excalidraw.com).
> El Mermaid de abajo es la versión que renderiza directo en GitHub y es la fuente versionada de referencia.

```mermaid
flowchart TD
    subgraph source["Fuente del análisis"]
        direction TB
        SRC["Source\n<i>Client + IG own-account  |  URL paste</i>"]
    end

    subgraph pipeline["Pipeline — ProfileAnalysis (Mundo 2)"]
        direction TB
        ANA["AnalyzeProfile\n<i>202 · record Requested</i>"]

        subgraph scrape["Scrape stage"]
            direction LR
            SCR["ScrapingProfile"]
            DEF{"Apify\ndefault actor"}
            BCK["backup actor\n<i>on fail</i>"]
            SCR -->|"default actor"| DEF
            DEF -.->|"fail → retry"| BCK
        end

        SCRAPED["ProfileScraped[Partial]\n<i>select resultType==profile; owner-filter; derive stats</i>"]
        ANLZ["Analyzing\n<i>org default AI · profile-analyzer.analysis.v1</i>"]
        ANA --> SCR
        SCRAPED --> ANLZ
    end

    subgraph terminal["Estados terminales"]
        direction LR
        OK["Analyzed"]
        FAIL["Failed"]
    end

    subgraph enduring["Datos perdurables — nivel Cliente"]
        direction TB
        DASH["Dashboard\n<i>stats + raw posts + comparison-if-client</i>"]
        IMP["Impute (per-field, confirmado)"]
        TARGETS["Brand Manual\nContent Plan\nGeneral Strategy"]
        DASH --> IMP --> TARGETS
    end

    SRC --> ANA
    DEF -->|"ok"| SCRAPED
    BCK -->|"ok"| SCRAPED
    ANLZ -->|"success"| OK
    ANLZ -->|"error"| FAIL
    OK --> DASH

    classDef sourceNode fill:#a5d8ff,stroke:#4a9eed,color:#0b2545;
    classDef pipelineNode fill:#d0bfff,stroke:#8b5cf6,color:#2e1065;
    classDef decisionNode fill:#fff3bf,stroke:#f59e0b,color:#7c2d12;
    classDef terminalOk fill:#b2f2bb,stroke:#22c55e,color:#14532d;
    classDef terminalFail fill:#ffd8d8,stroke:#ef4444,color:#7f1d1d;
    classDef enduringNode fill:#a5d8ff,stroke:#4a9eed,color:#0b2545;

    class SRC sourceNode;
    class ANA,SCR,SCRAPED,ANLZ pipelineNode;
    class DEF,BCK decisionNode;
    class OK terminalOk;
    class FAIL terminalFail;
    class DASH,IMP,TARGETS enduringNode;
```

## Leyenda

- **Fuente (azul)** — punto de entrada del pipeline; el nodo `Source` agrupa las dos rutas de entrada (client+own-account vs. URL paste).
- **Pipeline (morado)** — etapas de procesamiento en background: `AnalyzeProfile` (202 + encola el job), `ScrapingProfile` (Apify actor), `ProfileScraped[Partial]` (filtro por owner + derivación de stats), `Analyzing` (AI).
- **Decisión / fallback (ámbar)** — si el actor por defecto falla, se reintenta automáticamente con el backup actor (una sola vez, mismo run). Solo aplica al path `variantKey="profile"`. El pipeline de single-video no se ve afectado.
- **Estados terminales (verde/rojo)** — `Analyzed` (éxito) o `Failed` (ambos actores fallaron, o error de AI / config). Ambos persisten `ProviderDiagnosticsJson` con el shape PA-23 completo.
- **Datos perdurables (azul)** — tras `Analyzed`, el Dashboard expone stats + raw posts + comparison-if-client; el operador puede imputar campos detectados por IA al Brand Manual, Content Plan o General Strategy del cliente.

## Notas de implementación

| Aspecto | Decisión |
|---|---|
| Namespace | `Domain/Social/ProfileAnalysis/` — aislado de `SingleAnalyzer` (PA-REG-01) |
| Actor routing | `variantKey="profile"` en `actor_mappings_json` — NO reutiliza la entrada de single-video |
| Fallback | Automático: default → backup una vez en el mismo run (Design Decision 3) |
| AI flow key | `profile-analyzer.analysis.v1` — distinto de `single-analyzer.analysis.v1` |
| Diagnostics | `ProfileRunDiagnostics` POJO serializado en `ProviderDiagnosticsJson` en todos los terminales |
| Impute | Reutiliza comandos existentes de BrandManual/ContentPlan/GeneralStrategy — idempotente |
