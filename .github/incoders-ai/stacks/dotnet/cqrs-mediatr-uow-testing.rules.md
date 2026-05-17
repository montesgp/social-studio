# .NET CQRS / MediatR / UoW / Testing Rules (Compatibility Layer)

## Status
This file remains for backward compatibility.
Primary global engineering standards now live in:
- `.github/incoders-ai/core/best-practice.rules.md`

## .NET-specific supplement
- In .NET backends, use MediatR + validation pipeline for CQRS slices when orchestration complexity justifies it.
- Keep Application layer on abstractions (`IUnitOfWork`, repositories, ports), Infrastructure on EF/provider details.
- Do not inject `DbContext` directly into API/Application handlers.
