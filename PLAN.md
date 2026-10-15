# Short URL API — Plano de Desenvolvimento

> Foco: REST bem feito + EF Core + Cache + Docker + Testes.
> Stack alvo: **.NET 8 (target `net8.0`), SDK 10 para tooling, ASP.NET Core Controllers, EF Core + SQLite (compatível SQL Server), IMemoryCache, Rate Limiting nativo, Swagger, xUnit, Docker, GitHub Actions.**
> Solution: **`ShortUrl.slnx` (formato novo)** — requer VS 2022 17.12+ ou SDK 10 no CLI. Runtime executado é sempre .NET 8 LTS.

## 1. Visão geral

Construir uma API que encurta URLs:

- `POST /api/urls` recebe `{"url": "https://site.com/artigo-muito-longo", "customAlias": null, "expiresInDays": 30}` e retorna `{"shortCode": "aB3x9Q", "shortUrl": "http://localhost:5000/aB3x9Q"}`
- `GET /{shortCode}` retorna **302 redirect** para a URL original + incrementa contador de cliques
- `GET /api/urls/{shortCode}` retorna estatísticas (URL original, createdAt, expiresAt, clickCount)
- `DELETE /api/urls/{shortCode}` remove (opcional, bom para mostrar verbo DELETE)

Escopo propositalmente pequeno. O valor está na **qualidade da execução**, não em features.

## 2. Conceitos necessários

### 2.1 Geração do short code
- Use **6-8 caracteres Base62** `[a-zA-Z0-9]`. 62^7 ≈ 3,5 trilhões de combinações — suficiente.
- Geração: `RandomNumberGenerator` (criptográfico, `System.Security.Cryptography`) e não `Random`.
- Risco de colisão: índice UNIQUE no banco + loop de retry (tenta gerar, se colidir por unique constraint, gera outro, max 3 tentativas).
- Alternativa simples: permitir `customAlias` (ex: `meu-portfolio`). Se informado, validar regex `^[a-zA-Z0-9-_]{3,30}$` e checar duplicidade com 409 Conflict.

### 2.2 Redirect 302 vs 301
- Use **302 Found** (temporário). 301 é cacheado agressivamente pelo browser e atrapalha expiração/analytics.
- No ASP.NET: `return Results.Redirect(url, permanent: false)`.

### 2.3 Expiração
- Campo `ExpiresAt` nullable. Se `GET /{code}` com data passada → **410 Gone** (mostra que você conhece status code além do básico).
- Limpeza: não precisa de job agora. Valida na leitura. Pode citar como melhoria futura (BackgroundService).

### 2.4 Cache
- `IMemoryCache` para `GET /{code}` (caminho quente). TTL curto, ex: 5 min.
- Estratégia: cache-aside. Busca no cache → se miss, busca no banco e popula. No incremento de cliques, atualiza banco (pode ser async/fire-and-forget simples, ou update direto — para projeto pequeno, update direto está ok).
- Cite no README que em produção usaria Redis/Distributed Cache.

### 2.5 Rate Limiting (.NET 8 nativo)
- Use `AddRateLimiter` com política Fixed Window: ex. 30 req/min por IP no `POST`. Evita spam/abuso.
- Isso impressiona porque pouca gente Júnior configura.

### 2.6 Validação e segurança mínima
- Validar com `Uri.TryCreate(url, UriKind.Absolute)` + só aceitar `http/https`.
- Bloquear localhost/rede interna básica para evitar SSRF em teste técnico (lista simples: rejeita `localhost`, `127.0.0.1`, `10.*`, `192.168.*` — pode ser simplificado, só mencione no README).
- Tamanho máximo da URL: 2048 chars.

## 3. Modelo de dados

```csharp
namespace ShortUrl.Domain; // projeto ShortUrl.Domain

public class ShortUrl
{
    public int Id { get; set; }
    public string ShortCode { get; set; } = null!; // UNIQUE, ex: "aB3x9Q"
    public string OriginalUrl { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public long ClickCount { get; set; }
}
```

- Índice: `HasIndex(x => x.ShortCode).IsUnique()`.
- Migrations: `dotnet ef migrations add InitialCreate`.

## 4. Contratos da API

### POST /api/urls → 201 Created
Request:
```json
{ "url": "https://example.com/artigo", "customAlias": null, "expiresInDays": 30 }
```
Response 201:
```json
{ "shortCode": "aB3x9Q", "shortUrl": "http://localhost:5000/aB3x9Q", "expiresAt": "2026-11-13T00:00:00Z" }
```
Erros: 400 (URL inválida), 409 (alias em uso).

### GET /{shortCode} → 302
Sucesso: redirect. Erros: 404 (não existe), 410 (expirado).

### GET /api/urls/{shortCode} → 200
```json
{ "shortCode": "aB3x9Q", "originalUrl": "https://...", "createdAt": "...", "expiresAt": "...", "clickCount": 12 }
```

### DELETE /api/urls/{shortCode} → 204
404 se não existir.

### GET /health → 200
Health check nativo do ASP.NET para Docker/Render.

## 5. Estrutura de pastas (Clean Architecture simplificada)

Usa 1 solution com 4 projetos:

```
short-url-api/
├── ShortUrl.slnx            # Solution formato novo (SDK 10 / VS 2022 17.12+). Target continua net8.0.
├── global.json              # Pina SDK 10+ para tooling (slnx), sem mudar o runtime (net8.0)
├── src/
│   ├── ShortUrl.Domain/         # Entidade ShortUrl + ICodeGenerator (porta). Núcleo, sem dependências
│   ├── ShortUrl.Api/            # Controllers, Program.cs, DI, RateLimiter, Swagger
│   ├── ShortUrl.Application/    # DTOs, ShortUrlOptions, IShortUrlService, ShortUrlService
│   ├── ShortUrl.Infrastructure/ # AppDbContext, EF configs, Base62CodeGenerator (impl. da porta)
├── tests/
│   └── ShortUrl.Tests/          # xUnit: CodeGenerator + Service (com InMemory ou SQLite in-memory) + 1 teste de integração com WebApplicationFactory
├── Dockerfile
├── .github/workflows/ci.yml
└── README.md
```

Regra de dependência:
- `Domain` depende de nada.
- `Infrastructure → Domain` (o `AppDbContext` mapeia a entidade).
- `Application → Infrastructure + Domain` (serviço usa `DbContext` e a entidade).
- `Api → Application + Infrastructure` (só faz o wiring de DI; Controller nunca toca `DbContext` direto, só `IShortUrlService`).

> Nota: o `Domain` foi extraído para um projeto próprio porque a entidade é usada por `Application` e `Infrastructure`. Se ficasse só no `Application`, o `Infrastructure` não a enxergaria (geraria dependência circular).

## 6. Passo a passo de execução

### Fase 0 — Setup (30 min)

> SDK vs Target: SDK 10 compila, mas cada `.csproj` usa `<TargetFramework>net8.0</TargetFramework>`.
> `dotnet new slnx` **não existe** como template no SDK 10.0.401 — o caminho oficial via CLI é criar `sln` e migrar com `dotnet sln migrate`.

```powershell
# 1. Trava o SDK 10+ para tooling (slnx), mantendo runtime net8.0 nos csproj
# global.json:
# { "sdk": { "version": "10.0.401", "rollForward": "latestMajor", "allowPrerelease": false } }

# 2. Solution legada + migração para slnx
dotnet new sln -n ShortUrl

# 3. Projetos sempre com --framework net8.0 (sem isso o SDK 10 cria net10.0 por padrão)
dotnet new webapi -n ShortUrl.Api -o src/ShortUrl.Api --no-https --framework net8.0
# Decisão travada: Controllers (mais familiar para recrutador). Não usar Minimal API aqui.
dotnet new classlib -n ShortUrl.Application -o src/ShortUrl.Application --framework net8.0
dotnet new classlib -n ShortUrl.Infrastructure -o src/ShortUrl.Infrastructure --framework net8.0
dotnet new xunit -n ShortUrl.Tests -o tests/ShortUrl.Tests --framework net8.0
dotnet sln ShortUrl.slnx add (Get-ChildItem -Recurse *.csproj)
# ATENÇÃO: SDK 10 puxa o EF mais novo (10.x) por padrão, que NÃO roda em net8.0 (erro NU1202).
# Sempre trave na linha 8.x, alinhada ao runtime instalado (8.0.31 aqui):
dotnet add src/ShortUrl.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.31
dotnet add src/ShortUrl.Api package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.31
# Design no projeto de startup (Api) para o dotnet-ef gerar migrations:
dotnet add src/ShortUrl.Api package Microsoft.EntityFrameworkCore.Design --version 8.0.31
dotnet tool install --global dotnet-ef --version 8.*
```

Validação da Fase 0: `dotnet build ShortUrl.slnx` passa e `dotnet test ShortUrl.slnx` roda (mesmo sem testes ainda).

### Fase 1 — Domínio + Banco (1h)
1. Criar projeto `ShortUrl.Domain` (classlib, net8.0) e a entidade `ShortUrl` lá.
2. `Infrastructure` e `Application` referenciam `Domain`.
3. Criar `AppDbContext` (em `Infrastructure/Persistence`) com mapeamento Fluent + índice único em `ShortCode`; registrar no `Program.cs` com `UseSqlite`.
4. `dotnet ef migrations add InitialCreate --project src/ShortUrl.Infrastructure --startup-project src/ShortUrl.Api --output-dir Persistence/Migrations`
5. Testar `dotnet ef database update` (gera `shorturl.db`, já ignorado no `.gitignore`).

### Fase 2 — Regra de negócio (2h)
1. `ICodeGenerator` + implementação Base62 com `RandomNumberGenerator`.
2. `IShortUrlService` com métodos `CreateAsync`, `GetOriginalAsync` (incrementa clique), `GetStatsAsync`, `DeleteAsync`.
3. Validação de URL + alias + expiração aqui (não no controller).

### Fase 3 — API (2h)
1. Controllers: `UrlsController` (POST/GET stats/DELETE) + redirect no `Program.cs` ou `RedirectController` (`GET /{code}`).
2. `Results.Redirect` / `Redirect()` com 302.
3. Global exception handler + ProblemDetails.
4. Swagger com exemplos de request.
5. Rate Limiter no POST + HealthChecks (`AddHealthChecks`).

### Fase 4 — Cache (1h)
1. `AddMemoryCache`, decora leitura do redirect.
2. Medir: sem cache vs com cache (cite no README).

### Fase 5 — Testes (2h)
Mínimo que impressiona (5-8 testes):
- CodeGenerator gera tamanho certo e só Base62.
- Service rejeita URL inválida.
- Service retorna 409 em alias duplicado.
- Redirect incrementa ClickCount.
- Expirado retorna null/Expired (service) → controller mapeia para 410.
- 1 teste de integração POST → GET redirect com `WebApplicationFactory`.

### Fase 6 — Docker + CI (1h)
Dockerfile multi-stage:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/ShortUrl.Api -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ShortUrl.Api.dll"]
```
CI (`.github/workflows/ci.yml`): `dotnet restore → build → test` no push. Badge verde no README.
> CI precisa do **SDK 10** (para entender `slnx`) **+ runtime/SDK 8** (para rodar `net8.0`). No `actions/setup-dotnet@v4` use `dotnet-version: | 8.0.x 10.0.x`.

### Fase 7 — README + polish (1h)
Usar o `short-url-api-README.md` deste plano como base. Adicionar print do Swagger, comandos de run, tabela de endpoints, decisões técnicas (por que 302, por que IMemoryCache e não Redis agora).

## 7. Comandos do dia a dia

```powershell
dotnet run --project src/ShortUrl.Api
dotnet watch run --project src/ShortUrl.Api
dotnet test
dotnet ef migrations add NomeDaMigration --project src/ShortUrl.Infrastructure --startup-project src/ShortUrl.Api
docker build -t shorturl .
docker run -p 5000:8080 shorturl
```

Arquivo `.http` (VS Code REST Client) para testar sem Postman — deixe na raiz, recrutador gosta:
```http
POST http://localhost:5000/api/urls
Content-Type: application/json

{ "url": "https://dotnet.microsoft.com", "expiresInDays": 30 }
```

## 8. O que NÃO fazer (para manter pequeno)

- Sem autenticação neste projeto (isso é o projeto 2).
- Sem frontend. Swagger basta.
- Sem Redis/Postgres agora. SQLite + IMemoryCache é suficiente e roda em qualquer lugar.
- Sem analytics complexos (gráficos, por dia). Só `ClickCount`.

## 9. Melhorias futuras (seção Roadmap do README)

- [ ] Alias customizado reservado + autenticação JWT (liga com projeto 2)
- [ ] Redis distributed cache
- [ ] BackgroundService para limpar expirados
- [ ] Trocar SQLite por SQL Server via connection string (mostrar que EF abstrai)
- [ ] Deploy no Fly.io/Render + URL pública no README

## 10. Checklist de pronto (Definição de Feito)

- [ ] POST cria e retorna 201 com `Location` header
- [ ] GET redirect 302 funciona e conta clique
- [ ] Expirado retorna 410, inexistente 404, alias duplicado 409
- [ ] Swagger abre e permite testar
- [ ] `dotnet test` passa (≥5 testes)
- [ ] CI verde no GitHub
- [ ] `docker build + docker run` funciona
- [ ] README com como-rodar em 3 comandos
- [ ] `.gitignore` dotnet padrão, sem `bin/`, `obj/`, `*.db` commitados

## 11. O que estudar (se travar)

1. Minimal vs Controllers: https://learn.microsoft.com/aspnet/core/fundamentals/apis
2. EF Core SQLite + Migrations: https://learn.microsoft.com/ef/core/
3. Rate Limiting .NET 8: `builder.Services.AddRateLimiter(...)` + `[EnableRateLimiting]`
4. `RandomNumberGenerator.GetString()` — aceita alfabeto custom (perfeito p/ Base62)
5. `WebApplicationFactory` para teste de integração (1 teste basta)
