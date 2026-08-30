# Hive — Arquitectura general (capas + persistencia)

Vista de capas hexagonales end-to-end: de dónde entra la data (scraping, Frontend), cómo cruza
Api → Infrastructure → Application → Domain, y dónde se persiste hoy (Postgres, un único store).

La fuente editable es [`hive-architecture-overview.drawio`](./hive-architecture-overview.drawio) (draw.io).
Para diagramas de actividad/flujo puntuales se sigue usando Excalidraw (ver `*-flow.excalidraw` en esta
misma carpeta); draw.io queda reservado para diagramas de arquitectura/diseño formal.

## Qué muestra

- **Capas** (arriba a abajo): Externo/Frontend → Api (`Incoders.Hive.ApiService`) → Infrastructure
  (adapters Apify/YouTube/ComfyUI/Postgres) → Application (puertos + orquestación, sin dependencias de
  infra) → Domain (agregados con lifecycle) → Persistencia/Docker.
- **Sin repositorio genérico**: cada agregado tiene su propio puerto (`IxxxRepository`, ~43 hoy). No
  hay `IRepository<T>`; eso hace directo enchufar un adapter alternativo detrás de un puerto puntual sin
  tocar Domain/Application.
- **Datos provisorios vs productivos** (resaltado en amarillo punteado): `CandidateObservation` /
  `NicheOpportunity` (Content Discovery / Niche Discovery) son read models de Application, append-only,
  sin lifecycle — se recalculan en cada corrida (ADR-0012 §7, ADR-0013 §N5). Cuando el usuario selecciona
  un candidato, se promueve a `SingleContentAnalysis` (agregado real de Domain, con lifecycle completo).
  Este es el punto de corte natural si algún día se evalúa un segundo store (NoSQL) para lo descartable —
  evaluado en 2026-08-29 y pospuesto: no hay evidencia de cuello de botella real, era anticipación de
  escala futura.
- **Infra actual**: Aspire `AppHost` solo orquesta `Incoders_Hive_ApiService`; `docker-compose.dev.yml`
  levanta un único Postgres 17 plano. Producción/staging usan Supabase. ComfyUI es un servicio externo,
  no containerizado en este repo.

## Mantenimiento

Editar el `.drawio` en [app.diagrams.net](https://app.diagrams.net) (abrir el archivo local) o vía el
MCP de draw.io. Mantener este `.md` sincronizado cuando cambie una capa o se sume/quite un store.
