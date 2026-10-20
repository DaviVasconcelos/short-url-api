# Short URL API

[English](README.md) · **Português**

[![CI](https://github.com/DaviVasconcelos/short-url-api/actions/workflows/ci.yml/badge.svg)](https://github.com/DaviVasconcelos/short-url-api/actions/workflows/ci.yml)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Tests](https://img.shields.io/badge/tests-23%20passing-brightgreen)
![License MIT](https://img.shields.io/badge/license-MIT-green)

Um encurtador de URLs minimalista e bem arquitetado, feito com **C# e ASP.NET Core 8**. Foco em REST bem feito, EF Core, cache e boas práticas de engenharia.

> **SDK vs runtime:** o tooling usa **.NET 10 SDK + `ShortUrl.slnx`** (requer VS 2022 17.12+), mas o código roda em **.NET 8 LTS** (`<TargetFramework>net8.0</TargetFramework>` em todos os projetos).

## Funcionalidades

- `POST /api/urls` — encurta uma URL (suporta alias customizado + expiração)
- `GET /{code}` — redirect 302 para a URL original + contagem de cliques
- `GET /api/urls/{code}` — estatísticas (URL original, createdAt, expiresAt, clickCount)
- `DELETE /api/urls/{code}` — remove uma URL curta
- Validação (apenas http/https, máx. 2048 chars), 409 em conflito de alias, 410 em link expirado
- Cache em memória no caminho quente de redirect, rate limiting de janela fixa na criação
- Swagger, HealthChecks (`/health`), Docker, CI no GitHub Actions, testes xUnit

## Stack

- **.NET 8 / ASP.NET Core** (Controllers) — `net8.0` em todos os `.csproj`
- **.NET 10 SDK + `ShortUrl.slnx`** apenas como tooling (solution no formato novo)
- **EF Core + SQLite** para dev local/testes e **PostgreSQL** para produção, cada um com suas próprias migrations
- **IMemoryCache**, **Rate Limiting** (nativo do .NET 8)
- **Swagger / Swashbuckle**, **xUnit**, **`WebApplicationFactory`** para o teste de integração
- **Docker** com build multi-stage

## Começando

Pré-requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download) (para o tooling do `slnx`) + [.NET 8 runtime/SDK](https://dotnet.microsoft.com/download) (target `net8.0`), VS 2022 17.12+ ou VS Code + C# Dev Kit, e opcionalmente Docker.

```bash
git clone https://github.com/DaviVasconcelos/short-url-api
cd short-url-api
dotnet run --project src/ShortUrl.Api
```

É isso: `dotnet run` restaura os pacotes, aplica as migrations do EF no startup e sobe a API em `http://localhost:5000` (Swagger em `/swagger`). Teste:

```bash
# 1. encurta uma URL -> 201 Created + shortCode
curl -X POST http://localhost:5000/api/urls \
  -H "Content-Type: application/json" \
  -d '{"url":"https://dotnet.microsoft.com/en-us/learn","expiresInDays":30}'

# 2. segue o redirect (302) e lê as estatísticas
curl -i http://localhost:5000/<shortCode>
curl http://localhost:5000/api/urls/<shortCode>
```

![Swagger UI](docs/images/swagger.png)

Com Docker (as migrations do EF são aplicadas automaticamente no startup):

```bash
docker build -t shorturl .
docker run -p 5000:8080 shorturl

# sobrescreve a URL base usada nos links curtos gerados
docker run -p 5000:8080 -e ShortUrl__BaseUrl=https://your-domain shorturl
```

O container escuta em `8080`; mapeie para qualquer porta do host. O banco SQLite fica dentro do container, então monte um volume (`-v shorturl-data:/app`) para persistir.

Rodar os testes:

```bash
dotnet test ShortUrl.slnx
```

## Testes

**23 testes xUnit** em quatro áreas:

- `Generators/` — tamanho e alfabeto do código Base62 e validação de argumento.
- `Services/` — validação de URL/alias, conflito de alias, incremento de clique, populate/evict do cache, expiração e not found. Rodam contra **SQLite in-memory** (não o provider EF InMemory, que não executa o `ExecuteUpdateAsync` do incremento de clique).
- `Integration/` — um `WebApplicationFactory<Program>` sobe a API real com um banco SQLite em memória e exercita `POST /api/urls` → `GET /{code}` (302) → stats (`clickCount = 1`).
- `Persistence/` — monta o modelo PostgreSQL sem banco e valida o DDL gerado (tabela, colunas, índice único).

## Referência da API

| Método | Rota | Sucesso | Descrição |
|--------|------|---------|-----------|
| POST | `/api/urls` | `201 Created` | Encurta a URL |
| GET | `/{code}` | `302 Found` | Redirect |
| GET | `/api/urls/{code}` | `200 OK` | Estatísticas |
| DELETE | `/api/urls/{code}` | `204 No Content` | Remove |
| GET | `/health` | `200 OK` | Health check |

Exemplo:

```http
POST /api/urls
Content-Type: application/json

{ "url": "https://dotnet.microsoft.com/en-us/learn", "customAlias": null, "expiresInDays": 30 }
```

```json
{
  "shortCode": "aB3x9Q",
  "shortUrl": "http://localhost:5000/aB3x9Q",
  "expiresAt": "2026-11-13T00:00:00Z"
}
```

Códigos de erro: `400` URL inválida, `404` não encontrado, `409` alias em uso, `410` expirado, `429` rate limited.

## Estrutura do projeto

```
ShortUrl.slnx               # Solution (formato XML novo, SDK 10 / VS 2022 17.12+)
global.json                 # Fixa o SDK .NET 10 para tooling; runtime continua net8.0
Dockerfile                  # Build multi-stage (sdk:8.0 -> aspnet:8.0)
.dockerignore
.github/workflows/ci.yml    # restore -> build -> test (+ docker build)
docs/images/swagger.png     # Print do Swagger UI usado neste README
src/
  ShortUrl.Domain/          # Entidade ShortUrl + porta ICodeGenerator (sem dependências)
  ShortUrl.Api/             # Controllers, Program.cs, DI, rate limiter, Swagger
  ShortUrl.Application/     # DTOs, ShortUrlOptions, IShortUrlService, ShortUrlService
  ShortUrl.Infrastructure/  # AppDbContext, migrations SQLite, adapter Base62CodeGenerator
  ShortUrl.Infrastructure.Postgres/ # PostgresAppDbContext + migration InitialPostgres
tests/
  ShortUrl.Tests/           # 23 testes xUnit (generators, service, integração, modelo Postgres)
```

Regra de dependência: `Api -> Application -> Infrastructure`, mais `Api -> Infrastructure.Postgres -> Infrastructure`, com `Application` e `Infrastructure` dependendo de `Domain`. Controllers nunca tocam `DbContext` diretamente — apenas `IShortUrlService`.

```mermaid
graph LR
  Api[ShortUrl.Api] --> Application[ShortUrl.Application]
  Api --> Infrastructure[ShortUrl.Infrastructure]
  Api --> Postgres[ShortUrl.Infrastructure.Postgres]
  Postgres --> Infrastructure
  Application --> Infrastructure
  Application --> Domain[ShortUrl.Domain]
  Infrastructure --> Domain
```

## Decisões técnicas

- **302 (não 301)** nos redirects para que expiração e contagem de cliques continuem funcionando (navegadores cacheiam 301 agressivamente).
- **Códigos Base62 de 7 chars** via `RandomNumberGenerator` (~3,5T combinações) + índice UNIQUE + retry em colisão.
- **IMemoryCache** (TTL de 5 min, `ShortUrl.CacheTtlSeconds`) no caminho de redirect. O cache-aside guarda apenas `originalUrl` + `expiresAt` (nunca `clickCount`), e o incremento de clique continua no banco via `ExecuteUpdateAsync`, então o `SELECT` quente é o que deixa de acontecer. Redis fica como stretch goal para cache distribuído.
- **SQLite** (padrão) para dev local sem setup e testes; **PostgreSQL** é opt-in via `Database:Provider=Postgres`. Cada provider tem suas próprias migrations, então o mesmo modelo é validado nos dois.
- **Serviço agnóstico de provider**: `ShortUrlService` depende apenas de `AppDbContext`; `AddDbContext<AppDbContext, PostgresAppDbContext>` troca a implementação.
- **Rate limiting de janela fixa** (30 req/min/IP no POST) para evitar abuso, retornando `429` com `Retry-After`.
- **Aliases reservados** (`api`, `urls`, `health`, `swagger`, `favicon.ico`) são rejeitados para não sombrear rotas do app.
- **`/health`** verifica o DbContext configurado (`AddDbContextCheck`) para orquestradores checarem readiness.
- **Migrations rodam no startup** (com retry), então `docker run` e deploys futuros não precisam de `dotnet ef database update` manual.

### Cache na prática

Com o log de comandos do EF habilitado (`Microsoft.EntityFrameworkCore.Database.Command: Information` no `appsettings.Development.json`), duas requisições `GET /{code}` consecutivas produziram:

- **Sem cache** (baseline): 2 `SELECT` + 2 `UPDATE`.
- **Com cache**: 1 `SELECT` + 2 `UPDATE` — o segundo redirect é servido da memória; só o contador de cliques ainda toca o banco.

O comportamento não muda: as duas requisições retornam `302` e o endpoint de stats reporta `clickCount = 2`.

## Configuração

`appsettings.json`:

```json
{
  "Database": { "Provider": "Sqlite" },
  "ConnectionStrings": {
    "Default": "Data Source=shorturl.db",
    "Postgres": ""
  },
  "ShortUrl": { "BaseUrl": "http://localhost:5000", "CodeLength": 7, "DefaultExpiryDays": 30, "CacheTtlSeconds": 300 }
}
```

SQLite é o padrão. Para rodar contra PostgreSQL localmente:

```bash
docker run --rm -d --name shorturl-pg \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=shorturl \
  -p 5432:5432 postgres:16-alpine

export Database__Provider=Postgres
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=shorturl;Username=postgres;Password=postgres"
dotnet run --project src/ShortUrl.Api
```

As migrations são aplicadas no startup. Cada provider mantém seu próprio histórico (`InitialCreate` para SQLite, `InitialPostgres` para PostgreSQL); nunca aponte um provider para as migrations do outro. Em produção, defina `Database__Provider=Postgres` e `ConnectionStrings__Postgres` como variáveis de ambiente/secrets.

## Roadmap

Veja `PLAN.md` §12 para a divisão detalhada das fases.

**Escopo ativo**
- [x] Provider PostgreSQL + migrations (produção)
- [ ] Auth JWT + links por usuário + listagem paginada
- [ ] Deploy de demo pública (Render Free + Neon)
- [ ] Documentação, testes e runbook de deploy

**Stretch**
- [ ] Cache distribuído com Redis
- [ ] BackgroundService para limpar links expirados
- [ ] Rate limiting por usuário

**Cortado**
- Exemplo local com SQL Server (redundante com PostgreSQL)

## Licença

MIT — veja [LICENSE](LICENSE). Sinta-se livre para usar como template.
