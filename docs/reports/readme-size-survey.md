# How big are real package READMEs? A download-ranked survey

**Question.** Our [README-liability sweep](readme-liability.md) showed the shipped README is a
token liability whose cost scales with its size. So: **how big are real package READMEs in
practice**, and is the liability hypothetical or routine?

**Method.** We took the **top 40 most-downloaded packages** in two strata and measured the README
that ships *inside the package* (the `<PackageReadmeFile>` the NuGet MCP and nuget.org surface —
*not* the often much larger GitHub README):

- **Microsoft-owned** — id prefix `System.` / `Microsoft.` / `Azure.`
- **Community** — everything else

Download ranks come from the NuGet search service (empty query is download-ordered); the declared
readme's name and byte size are read in one shot with
[`dotnet-inspect`](https://github.com/richlander/dotnet-inspect) —
`package <id> --path @readme --json` emits `{path, size, is_readme}`, resolving the *declared*
readme regardless of filename ([added in dotnet-inspect #891](https://github.com/richlander/dotnet-inspect/pull/891)).
Latest stable, 2026-06-20. Stratifying avoids the bias of a hand-picked list — a flat "top 40" is
dominated by Microsoft infra packages and hides the community distribution. Raw data
(`package`, `readme_file`, `size`, `is_readme`):
[`data/readme-survey-microsoft.tsv`](../../data/readme-survey-microsoft.tsv),
[`data/readme-survey-community.tsv`](../../data/readme-survey-community.tsv).

> **Naming gotcha (worth knowing on its own).** The in-package readme is *not* always `README.md` —
> it is whatever the `.nuspec` `<readme>` element points at. Across the 57 surveyed packages that
> ship one, **only 48 (84%) use `README.md` or `PACKAGE.md`**; the other **9 (~16%) deviate**:
>
> - **`PACKAGE.md`** (18 pkgs) — the .NET runtime / `System.*` convention (System.Text.Json, EF Core).
> - **`package-readme.md`** (Polly, the Swashbuckle sub-packages).
> - **lowercase `readme.md`** (Moq, Dapper) — bites case-sensitive filesystems and `==` checks.
> - **subdirectory paths** — `docs/package-readme.md` (Swashbuckle.AspNetCore),
>   `_content/README.md` (xunit). A root-only or `README.md`-literal scan misses these entirely:
>   our earlier `--readme`-based pass counted Swashbuckle.AspNetCore as *no readme*; resolving the
>   declared path found its `docs/package-readme.md`.
> - **a non-readme-named file** — coverlet.collector declares `VSTestIntegration.md` as its readme.
>
> A survey (or an agent) that greps for `README.md` undercounts and mispaths; resolve the *declared*
> readme (`--path @readme`) instead.

## Findings

| | Microsoft-owned (top 40) | Community (top 40) |
|---|---|---|
| Ship an in-package README | 29 / 39 | 28 / 39 |
| Ship **none** | 10 | 11 |
| Median size (of those that ship one) | **5.1 kB** | **3.9 kB** |
| Over 10 kB | 6 | 4 |
| Largest | Azure.Identity **25.9 kB** | OpenTelemetry.Api **25.4 kB** |

*(n=39 per bucket: one slot each was a metapackage/runtime shim with an empty root.)*

1. **In-package READMEs are mostly small — in both worlds.** Median ~4–5 kB; three-quarters sit
   under ~8 kB. The two distributions are strikingly similar, so this is not a "Microsoft is
   disciplined / community is sprawling" story — the `<PackageReadmeFile>` convention nudges *both*
   toward a concise, package-scoped readme distinct from the GitHub landing page. For the typical
   package the README liability is real but modest: a **~1–2.5× ratio** against a ~3.5 kB targeted
   `AGENTS.md`.
2. **A real heavy tail exists, but it's bounded.** The largest in the *download-ranked* top 40 top
   out near **~25 kB** (Azure.Identity, OpenTelemetry.Api, System.ClientModel 19.5 kB,
   Serilog.Settings.Configuration 19.5 kB). The xl arm of our liability sweep (74 kB) and packages
   like Refit (62 kB) are **outliers**, not the popular norm — worth modeling, but the routine
   liability is the 8–26 kB band, not 70 kB.
3. **~25–30% of popular packages ship no in-package README at all** — Microsoft (e.g.
   Microsoft.Extensions.Diagnostics.*, Microsoft.ApplicationInsights) and community alike
   (AWSSDK.Core, Castle.Core, CsvHelper, FluentAssertions, Humanizer.Core, Google.Protobuf). For
   these an agent gets *nothing* from the package today — the cleanest case for grounding: net-new
   value, no README to displace, no liability to trade against.

**Takeaway for grounding.** The opportunity is two-shaped, and the same in both ecosystems: (1) for
the **large-README tail** (8–26 kB routine, 60 kB+ outliers), grounding is *README avoidance* —
replace the liability with a flat ~3.5 kB targeted doc; (2) for the **~quarter that ship no
README**, grounding is *net-new context*. The fat middle (small READMEs) is where grounding must
clear the [Pareto gate](../authoring-principles.md) on its own merits, since the README it would
displace is already cheap.

## Caveats

- **Download rank clusters by family.** The community top 40 is heavy with `Serilog.*` and
  `Swashbuckle.AspNetCore.*` sub-packages, and the Microsoft list with `Microsoft.Extensions.*` —
  popular ecosystems contribute many ranked ids. The medians are representative; treat the
  per-package rows as illustrative, not 40 independent libraries.
- **In-package only.** Many packages' *GitHub* READMEs are far larger. The in-package figure is the
  right one for the NuGet-MCP delivery path this repo studies, where the agent is served the
  package's own readme, not the repository's.
- A few ranked entries are runtime shims / SNI / manifest packages with no human-facing content;
  they count as "none."

## What's *in* the large READMEs? (composition spot check)

Size alone doesn't prove the content is low-value for an agent, so we read the section structure of
the four largest in the survey. The byte split (level-2 sections):

| Package | total | install / framing / boilerplate | usage & examples |
|---------|------:|-------------------------------:|-----------------:|
| Azure.Identity | 25.9 kB | ~30% (incl. **Contributing 4.5 kB**) | ~70% |
| Azure.Storage.Blobs | 13.9 kB | ~48% (incl. **Contributing 2.7 kB**) | ~52% |
| OpenTelemetry.Api | 24.9 kB | ~33% | ~67% |
| System.Text.Json | 8.1 kB | ~16% | ~84% |

Two things stand out, and they refine the naive "it's mostly installation" intuition:

- **Installation itself is tiny** — Azure.Identity's whole "Getting started" (with install) is
  ~0.8 kB; OpenTelemetry's `## Installation` is **68 bytes**. Install is the *clearest* waste for an
  agent (the dependency is already referenced), but it is not where the bytes go.
- **The bulk is human-onboarding prose, not agent-actionable gotchas.** A meaningful slice is
  framing/boilerplate — "Getting started," "Key concepts," "Next steps," and especially
  **Contributing** (2–4 kB of fixed boilerplate in the Azure-SDK template) — and *most of the rest is
  general usage/examples the model is already resident on*. Across these READMEs, the fraction that
  is a non-obvious, version-specific footgun (the thing grounding targets) is small. That is the
  liability: an agent pays for the whole document to reach the thin slice it actually needed — if any
  of it is even present.


Ratio = README bytes ÷ a 3.5 kB (3584 B) targeted `AGENTS.md`.

| Package | README (kB) | ratio |
|---------|------------:|------:|
| Azure.Identity | 25.9 | 7.4× |
| System.ClientModel | 19.5 | 5.6× |
| Azure.Security.KeyVault.Secrets | 16.3 | 4.7× |
| Azure.Storage.Blobs | 13.9 | 4.0× |
| Azure.Core | 13.9 | 4.0× |
| System.Diagnostics.PerformanceCounter | 10.2 | 2.9× |
| Microsoft.Data.SqlClient | 9.4 | 2.7× |
| System.Text.Json | 8.4 | 2.4× |
| Microsoft.Identity.Client.Extensions.Msal | 7.8 | 2.2× |
| Microsoft.Identity.Client | 7.8 | 2.2× |
| Azure.Storage.Common | 6.7 | 1.9× |
| Microsoft.OpenApi | 6.4 | 1.8× |
| Microsoft.IdentityModel.Abstractions | 6.2 | 1.8× |
| Microsoft.Extensions.Logging | 5.6 | 1.6× |
| System.Formats.Asn1 | 5.1 | 1.5× |
| System.Diagnostics.EventLog | 4.8 | 1.4× |
| Microsoft.Extensions.Http | 4.5 | 1.3× |
| System.CodeDom | 4.5 | 1.3× |
| Microsoft.EntityFrameworkCore | 3.5 | 1.0× |
| Microsoft.AspNetCore.Mvc.NewtonsoftJson | 3.3 | 1.0× |
| System.Memory.Data | 3.2 | 0.9× |
| Microsoft.Win32.SystemEvents | 3.1 | 0.9× |
| System.Windows.Extensions | 3.0 | 0.8× |
| Microsoft.Extensions.Hosting | 2.7 | 0.8× |
| System.Drawing.Common | 2.5 | 0.7× |
| Microsoft.Bcl.AsyncInterfaces | 2.3 | 0.6× |
| System.Runtime.Caching | 2.3 | 0.6× |
| Microsoft.Extensions.DependencyInjection | 2.3 | 0.6× |
| System.Threading.Channels | 2.1 | 0.6× |
| System.Security.Cryptography.Pkcs | none | — |
| Microsoft.NET.Sdk.Aspire.Manifest-8.0.100 | none | — |
| Microsoft.Extensions.Logging.EventLog | none | — |
| Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions | none | — |
| Microsoft.Extensions.Diagnostics.HealthChecks | none | — |
| Microsoft.Extensions.Diagnostics.Abstractions | none | — |
| Microsoft.Extensions.Diagnostics | none | — |
| Microsoft.Extensions.ApiDescription.Server | none | — |
| Microsoft.Data.SqlClient.SNI.runtime | none | — |
| Microsoft.ApplicationInsights | none | — |

## Survey — Community (top 40 by downloads)

| Package | README (kB) | ratio |
|---------|------------:|------:|
| OpenTelemetry.Api | 25.4 | 7.3× |
| Serilog.Settings.Configuration | 19.5 | 5.6× |
| Serilog.AspNetCore | 12.2 | 3.5× |
| coverlet.collector | 12.1 | 3.5× |
| Serilog.Extensions.Logging | 9.1 | 2.6× |
| Serilog.Sinks.File | 8.3 | 2.4× |
| Moq | 7.8 | 2.2× |
| Serilog | 7.8 | 2.2× |
| Serilog.Sinks.Console | 7.6 | 2.2× |
| Serilog.Formatting.Compact | 7.4 | 2.1× |
| Grpc.Net.Client | 6.7 | 1.9× |
| NUnit | 5.7 | 1.6× |
| xunit | 5.0 | 1.4× |
| Serilog.Extensions.Hosting | 4.0 | 1.1× |
| AutoMapper | 3.8 | 1.1× |
| Serilog.Sinks.Debug | 2.7 | 0.8× |
| Npgsql | 2.1 | 0.6× |
| FluentValidation | 2.0 | 0.6× |
| Newtonsoft.Json | 1.9 | 0.5× |
| Pipelines.Sockets.Unofficial | 1.4 | 0.4× |
| Dapper | 1.3 | 0.4× |
| Polly | 1.2 | 0.4× |
| Swashbuckle.AspNetCore.SwaggerUI | 1.2 | 0.3× |
| Swashbuckle.AspNetCore.SwaggerGen | 1.2 | 0.3× |
| Swashbuckle.AspNetCore.Swagger | 1.2 | 0.3× |
| Swashbuckle.AspNetCore | 1.2 | 0.3× |
| StackExchange.Redis | 0.6 | 0.2× |
| Grpc.Core.Api | 0.1 | 0.0× |
| runtime.win-x86.runtime.native.System.Data.SqlClient.sni | none | — |
| runtime.win-x64.runtime.native.System.Data.SqlClient.sni | none | — |
| runtime.win-arm64.runtime.native.System.Data.SqlClient.sni | none | — |
| Newtonsoft.Json.Bson | none | — |
| Humanizer.Core | none | — |
| Grpc.Net.Common | none | — |
| Google.Protobuf | none | — |
| FluentAssertions | none | — |
| CsvHelper | none | — |
| Castle.Core | none | — |
| AWSSDK.Core | none | — |
