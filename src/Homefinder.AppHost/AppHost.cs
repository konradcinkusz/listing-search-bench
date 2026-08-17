// ─────────────────────────────────────────────────────────────────────────────
//  The composition root (P1).
//
//  One command brings the system up: `dotnet run --project src/Homefinder.AppHost`.
//  Every resource the system needs is declared here, with the edges between them.
//
//  Two rules that carry over from the worked example this repository mirrors,
//  both relevant already:
//
//   • The AppHost is not the production topology. Production is described by the
//     platform's own configuration, not by this file.
//
//   • Nothing here carries a secret. The demonstrated path needs none at all — the
//     in-memory fixture index is the default (ADR-0002), so a fresh clone runs against
//     an empty .env. Elasticsearch is opt-in, wired through docker-compose.yml for
//     local development, and is never what CI runs against.
// ─────────────────────────────────────────────────────────────────────────────

var builder = DistributedApplication.CreateBuilder(args);

var searchService = builder.AddProject<Projects.Homefinder_SearchService>("search")
    .WithHttpHealthCheck("/health");

_ = searchService;

builder.Build().Run();
