# README size survey: Microsoft-prefix vs community NuGet packages

Generated from the two repo artifacts:

- `data/readme-survey-microsoft-top1000.json` - Top 1,000 packages whose IDs start with `Microsoft.`, `Azure.`, or `System.`.
- `data/readme-survey-community-top1000.json` - Top 1,000 packages whose IDs do not start with `Microsoft.`, `Azure.`, or `System.`.

README measurements use `dnx dotnet-inspect -y -- package <id>@<version> --path @readme --jsonl`. The artifacts also record literal root `README.md` using `--path README.md`.

## Summary

| Population | Packages | Package README present | Missing | Literal root README.md | Median | P75 | P90 | Max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Microsoft/Azure/System | 1,000 | 376 (37.6%) | 624 | 181 | 2,181 B | 5,241 B | 9,797 B | 43,792 B |
| Community | 1,000 | 501 (50.1%) | 499 | 390 | 2,927 B | 8,174 B | 16,970 B | 93,749 B |

| Population | Package README paths among README-present packages |
|---|---:|
| Microsoft/Azure/System | `README.md`: 167; `PACKAGE.md`: 169; other path: 40 |
| Community | `README.md`: 293; `PACKAGE.md`: 8; other path: 200 |

## Broad package README size buckets

These tables include all 1,000 packages in each population. `none` means `--path @readme` returned no package README.

### Microsoft/Azure/System

| Bucket | Packages | Share | Literal root README.md | Largest package in bucket | Largest size |
|---|---:|---:|---:|---|---:|
| none | 624 | 62.4% | 0 | - | - |
| 1-999 B | 112 | 11.2% | 64 | `Microsoft.CodeAnalysis.NetAnalyzers` | 984 B |
| 1-1.9 kB | 65 | 6.5% | 20 | `Microsoft.AspNetCore.Owin` | 1,956 B |
| 2-3.9 kB | 87 | 8.7% | 14 | `Microsoft.Extensions.Configuration` | 3,981 B |
| 4-7.9 kB | 58 | 5.8% | 32 | `Microsoft.Identity.Client` | 7,964 B |
| 8-15.9 kB | 34 | 3.4% | 31 | `Microsoft.Extensions.ServiceDiscovery` | 15,956 B |
| 16-31.9 kB | 17 | 1.7% | 17 | `Azure.Messaging.EventHubs.Processor` | 28,083 B |
| 32-63.9 kB | 3 | 0.3% | 3 | `Microsoft.Identity.Abstractions` | 43,792 B |
| >=64 kB | 0 | 0.0% | 0 | - | - |

### Community

| Bucket | Packages | Share | Literal root README.md | Largest package in bucket | Largest size |
|---|---:|---:|---:|---|---:|
| none | 499 | 49.9% | 0 | - | - |
| 1-999 B | 69 | 6.9% | 59 | `Asp.Versioning.Abstractions` | 992 B |
| 1-1.9 kB | 114 | 11.4% | 92 | `runtime.osx-x64.runtime.native.System.IO.Ports` | 1,999 B |
| 2-3.9 kB | 115 | 11.5% | 63 | `Apache.Avro` | 3,957 B |
| 4-7.9 kB | 76 | 7.6% | 56 | `Moq` | 7,969 B |
| 8-15.9 kB | 73 | 7.3% | 69 | `Destructurama.Attributed` | 15,854 B |
| 16-31.9 kB | 34 | 3.4% | 33 | `MailKit` | 28,367 B |
| 32-63.9 kB | 13 | 1.3% | 11 | `Serilog.Sinks.MSSqlServer` | 56,783 B |
| >=64 kB | 7 | 0.7% | 7 | `jose-jwt` | 93,749 B |

## Percentile buckets, P10 through P100

These tables are over the README-present population only. Counts are non-cumulative packages in each percentile bucket; upper size is the nearest-rank size at that percentile.

### Microsoft/Azure/System (376 packages with package READMEs)

| Percentile bucket | Packages in bucket | Size range | Upper size |
|---|---:|---:|---:|
| P0-P10 | 38 | 24 B - 438 B | 438 B |
| P10-P20 | 38 | 466 B - 732 B | 732 B |
| P20-P30 | 37 | 732 B - 1,011 B | 1,011 B |
| P30-P40 | 38 | 1,036 B - 1,637 B | 1,637 B |
| P40-P50 | 37 | 1,640 B - 2,181 B | 2,181 B |
| P50-P60 | 38 | 2,191 B - 2,849 B | 2,849 B |
| P60-P70 | 38 | 2,871 B - 3,981 B | 3,981 B |
| P70-P80 | 37 | 4,033 B - 6,350 B | 6,350 B |
| P80-P90 | 38 | 6,350 B - 9,797 B | 9,797 B |
| P90-P100 | 37 | 9,797 B - 43,792 B | 43,792 B |

### Community (501 packages with package READMEs)

| Percentile bucket | Packages in bucket | Size range | Upper size |
|---|---:|---:|---:|
| P0-P10 | 51 | 24 B - 759 B | 759 B |
| P10-P20 | 50 | 788 B - 1,106 B | 1,106 B |
| P20-P30 | 50 | 1,107 B - 1,423 B | 1,423 B |
| P30-P40 | 50 | 1,430 B - 2,224 B | 2,224 B |
| P40-P50 | 50 | 2,231 B - 2,927 B | 2,927 B |
| P50-P60 | 50 | 2,927 B - 4,021 B | 4,021 B |
| P60-P70 | 50 | 4,059 B - 6,390 B | 6,390 B |
| P70-P80 | 50 | 6,419 B - 10,239 B | 10,239 B |
| P80-P90 | 50 | 10,239 B - 16,970 B | 16,970 B |
| P90-P100 | 50 | 17,750 B - 93,749 B | 93,749 B |

## Tail percentile buckets, P90 through P100

These tables expand the top decile of the README-present population into five 2-percentile-point buckets.

### Microsoft/Azure/System

| Percentile bucket | Packages in bucket | Size range | Upper size |
|---|---:|---:|---:|
| P90-P92 | 7 | 9,797 B - 10,848 B | 10,848 B |
| P92-P94 | 8 | 11,665 B - 14,410 B | 14,410 B |
| P94-P96 | 7 | 15,721 B - 19,931 B | 19,931 B |
| P96-P98 | 8 | 22,044 B - 27,971 B | 27,971 B |
| P98-P100 | 7 | 27,993 B - 43,792 B | 43,792 B |

### Community

| Percentile bucket | Packages in bucket | Size range | Upper size |
|---|---:|---:|---:|
| P90-P92 | 10 | 17,750 B - 19,785 B | 19,785 B |
| P92-P94 | 10 | 19,916 B - 22,199 B | 22,199 B |
| P94-P96 | 10 | 22,229 B - 28,367 B | 28,367 B |
| P96-P98 | 10 | 32,796 B - 52,721 B | 52,721 B |
| P98-P100 | 10 | 53,910 B - 93,749 B | 93,749 B |

## Largest package READMEs

### Microsoft/Azure/System

| Rank | Package | Path | Size |
|---:|---|---|---:|
| 1 | `Microsoft.Identity.Abstractions` | `README.md` | 43,792 B |
| 2 | `Azure.Messaging.ServiceBus` | `README.md` | 34,053 B |
| 3 | `Azure.Search.Documents` | `README.md` | 32,078 B |
| 4 | `Azure.Messaging.EventHubs.Processor` | `README.md` | 28,083 B |
| 5 | `Microsoft.ClearScript` | `ReadMe.md` | 27,993 B |
| 6 | `Microsoft.ClearScript.Core` | `ReadMe.md` | 27,993 B |
| 7 | `Microsoft.ClearScript.V8` | `ReadMe.md` | 27,993 B |
| 8 | `Azure.ResourceManager` | `README.md` | 27,971 B |
| 9 | `Azure.Messaging.EventHubs` | `README.md` | 27,861 B |
| 10 | `Azure.Identity` | `README.md` | 26,563 B |

### Community

| Rank | Package | Path | Size |
|---:|---|---|---:|
| 1 | `jose-jwt` | `README.md` | 93,749 B |
| 2 | `Refit` | `README.md` | 92,692 B |
| 3 | `Refit.HttpClientFactory` | `README.md` | 92,692 B |
| 4 | `Refit.Newtonsoft.Json` | `README.md` | 92,692 B |
| 5 | `RestEase` | `README.md` | 82,482 B |
| 6 | `Audit.NET` | `README.md` | 77,290 B |
| 7 | `VaultSharp` | `README.md` | 66,832 B |
| 8 | `Serilog.Sinks.MSSqlServer` | `README.md` | 56,783 B |
| 9 | `MimeKit` | `docs/README.md` | 53,910 B |
| 10 | `MimeKitLite` | `docs/README.md` | 53,910 B |

