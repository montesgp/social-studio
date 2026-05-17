# PostgreSQL Provider Instructions

## Rules
- Model constraints at database level when they protect invariants.
- Keep provider-specific SQL behind repository/query abstractions.
- Use lower `snake_case` for PostgreSQL schemas, tables, columns, indexes, constraints, functions and policies.
- In .NET/EF Core projects, preserve idiomatic C# names in code and map the relational model to PostgreSQL naming explicitly via model configuration or a naming convention.
- Avoid mixed-case or quoted identifiers in migrations unless a third-party integration strictly requires them.
- Configure ORM migration history tables with lower-case names, for example EF Core `__ef_migrations_history`.
