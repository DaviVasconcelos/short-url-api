# Short URL API — Plano de Desenvolvimento

> Foco: REST bem feito + EF Core + Cache + Docker + Testes.
> Stack alvo: **.NET 8 (target `net8.0`), SDK 10 para tooling, ASP.NET Core Controllers, EF Core + SQLite (compatível SQL Server), IMemoryCache, Rate Limiting nativo, Swagger, xUnit, Docker, GitHub Actions.**
> Solution: **`ShortUrl.slnx` (formato novo)** — requer VS 2022 17.12+ ou SDK 10 no CLI. Runtime executado é sempre .NET 8 LTS.
>
> **Status (2026-10-29):** **Corte enxuto concluído** — 0a, 1a, 3a, 3b, 3c, 3d, 4a, 4b e **7a** entregues (91 testes verdes; PostgreSQL em produção no Neon, auth JWT, ownership, listagem paginada, container readiness, demo pública em `https://shorturl-davi.onrender.com` e documentação consolidada com `GLOSSARY.md` + `docs/adr/`). O que resta são apenas itens **stretch** (Redis, cleanup de expirados, rate limit por usuário) e o SQL Server, que foi cortado. As seções 1-11 são o plano histórico; o plano ativo é a seção **12. Plano v2 - Evolução para portfólio (custo zero)**.

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
- `IMemoryCache` para `GET /{code}` (caminho quente). TTL curto, configurável (`ShortUrlOptions.CacheTtlSeconds`, default 300 = 5 min).
- Estratégia: **cache-aside**. Busca no cache → se miss, busca no banco e popula.
- **O que cachear:** apenas `originalUrl` + `expiresAt` (o necessário para decidir 302/410). **Nunca** cachear `ClickCount`, senão a contagem congela.
- **Incremento de clique:** continua indo ao banco, mas via `ExecuteUpdateAsync` (`UPDATE ShortUrls SET ClickCount = ClickCount + 1 WHERE ShortCode = @code`) — sem `SELECT` nem change tracker. Assim o cache economiza de fato a query quente (o `SELECT`).
- **Invalidação:** `ShortUrlService.DeleteAsync` remove a chave. Criação não precisa invalidar (código novo). TTL cobre o resto.
- Chave sugerida: `url:{code}`.
- Cite no README que em produção usaria Redis/Distributed Cache.

### 2.5 Rate Limiting (.NET 8 nativo)
- Use `AddRateLimiter` com política Fixed Window: ex. 30 req/min **por IP** no `POST` (`RateLimitPartition.GetFixedWindowLimiter`, chave = `RemoteIpAddress`). Evita spam/abuso.
- `OnRejected` → 429 + header `Retry-After`.
- Nota de produção: atrás de proxy/load balancer, `RemoteIpAddress` é o do proxy; seria preciso `ForwardedHeaders`/`X-Forwarded-For`.
- Isso impressiona porque pouca gente Júnior configura.

### 2.6 Validação e segurança mínima
- Validar com `Uri.TryCreate(url, UriKind.Absolute)` + só aceitar `http/https`.
- Bloquear localhost/rede interna básica para evitar SSRF em teste técnico (lista simples: rejeita `localhost`, `127.0.0.1`, `10.*`, `172.16-31.*`, `192.168.*`, `169.254.*`).
- Tamanho máximo da URL: 2048 chars.
- Reservar aliases que colidem com rotas do app: `api`, `urls`, `health`, `swagger`, `favicon.ico` → rejeitar como `InvalidAlias` (400).

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
1. Controllers: `UrlsController` (`POST`/`GET stats`/`DELETE` em `api/urls`) + `RedirectController` (`GET /{code}` → 302, oculto do Swagger).
2. `Redirect()` com 302 (permanente: false). `POST` → `CreatedAtAction` (201 + `Location`).
3. Global exception handler (`IExceptionHandler`) + `AddProblemDetails`.
4. Swagger com XML comments (`GenerateDocumentationFile`) + `[ProducesResponseType]`.
5. Rate Limiter (Fixed Window 30/min por IP no POST) + HealthChecks (`AddHealthChecks().AddDbContextCheck<AppDbContext>()` em `/health`).
6. Alinhar porta local para 5000 (`launchSettings.json`) com `ShortUrl.BaseUrl`.

### Fase 4 — Cache (1h)

Design já decidido (ver §2.4). Implementar nesta ordem:
1. `builder.Services.AddMemoryCache();` no `Program.cs`.
2. Adicionar `CacheTtlSeconds` (default 300) em `ShortUrlOptions`.
3. Injetar `IMemoryCache` no construtor de `ShortUrlService` (é o seam de DI; os testes da Fase 5 serão escritos depois, já com esse construtor).
4. `GetOriginalAsync` (cache-aside):
   - chave `url:{code}`; guardar `{ originalUrl, expiresAt }` (record leve, ex. `CachedShortUrl`).
   - hit → valida expiração com o `expiresAt` cacheado; não expirado → incrementa clique via `ExecuteUpdateAsync` e retorna `Found`.
   - miss → `SELECT` no banco; `null` → `NotFound`; expirado → `Expired`; senão popula o cache (TTL) e incrementa clique.
5. `DeleteAsync` → `_cache.Remove($"url:{code}")` após remover do banco.
6. **Medir sem cache vs com cache**: rodar a API com log de comandos do EF (`"Microsoft.EntityFrameworkCore.Database.Command": "Information"` em `appsettings.Development.json`), disparar `GET /{code}` duas vezes e comparar a contagem de `SELECT` (2x sem cache → 1x com cache). Registrar o resultado no README.

Validação: build verde + smoke test do `.http` (302 continua, `ClickCount` continua incrementando na 2ª chamada).

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

## 12. Plano v2 — Evolução para portfólio (custo zero)

> Aprovado em 2026-10-19; revisado para um **corte enxuto** com foco em concluir o portfólio rapidamente. As seções 1-11 permanecem como registro histórico das Fases 0-7.

### 12.1 Decisões travadas

- **Escopo ativo:** PostgreSQL, auth JWT + ownership, listagem paginada, deploy e documentação/testes.
- **Stretch:** Redis, cleanup de expirados e rate limit por usuário.
- **Cortado:** SQL Server local (redundante com PostgreSQL) e Fly.io.
- **Deploy:** Render Free Web Service + Neon Free Postgres. Custo **$0** e sem cartão de crédito. Demo: `https://shorturl-davi.onrender.com` (Blueprint em `render.yaml`).
- **Banco local/testes:** SQLite (mantém a suíte rápida e sem dependência externa).
- **Banco de produção:** PostgreSQL (Neon).
- **Auth:** JWT + ownership. Criação anônima continua; link autenticado ganha dono; `DELETE` exige o dono.
- **Links anônimos:** não podem ser deletados pela API; chamada sem token recebe `401` e usuário autenticado recebe `403`.
- **Stats (`GET /api/urls/{code}`):** continuam públicos.
- **Listagem (`GET /api/urls`):** exige autenticação e retorna apenas os links do usuário.
- **Cache:** manter o `IMemoryCache` atual; seam + Redis ficam como stretch.
- **Migrations:** SQLite em `ShortUrl.Infrastructure`; PostgreSQL em projeto separado.
- **Documentação:** README/AGENTS atualizados ao fim de cada entrega ativa; consolidação na Fase 7.

### 12.2 Macrofases e entregas

| Macro | Entrega | Status | Aceite | Depende |
|---|---|---|---|---|
| **0. Baseline** | **0a** Branch + suíte atual | **Feito** | `feat/roadmap-v2` criada; 22/22 testes verdes | — |
| **1. PostgreSQL** | **1a** Provider + projeto de migrations + `InitialPostgres` | **Feito** | API sobe com `Database:Provider=Postgres` e aplica migration | 0a |
|  | **1b** Compose local + job Postgres na CI + tool manifest | **Opcional rápido** | `dotnet ef database update` verde na CI | 1a |
| **2. Cache seam** | **2a** `IShortUrlCache` + `CachedShortUrl` + `DistributedShortUrlCache` | **Stretch** | Cache abstrato compila e é resolvido pela DI | 0a |
|  | **2b** `ShortUrlService` + testes com cache abstrato | **Stretch** | 22 testes verdes; redirect continua funcionando | 2a |
| **3. Auth** | **3a** `User`, hasher, `OwnerId` e migration | **Feito** | Schema novo aplicado; links existentes ficam com `OwnerId = null` | 1a |
|  | **3b** register/login/me + JWT + 401 | **Feito** | Fluxo de login e `me` funcionando; Swagger com Bearer; ownership concluído na 3c | 3a |
|  | **3c** Ownership no create/delete + política de anônimos | **Feito** | Dono deleta (`204`); sem token `401`; não-dono e link anônimo recebem `403`; código ausente `404` | 3b |
|  | **3d** `GET /api/urls` paginado | **Feito** | Lista só links do usuário com `page`/`pageSize` validados | 3c |
|  | **3e** Hardening | **Stretch** | Rate limit por usuário/IP, validação de `expiresInDays` e ProblemDetails | 3b |
| **4. Deploy** | **4a** Container readiness | **Feito** | Docker honra `$PORT`; `/health/live` e `/health`; `ForwardedHeaders`; Swagger flag; BaseUrl fallback | 1a |
|  | **4b** Render + Neon | **Feito** | URL pública no `onrender.com`; fluxo completo validado | 3c, 4a |
| **5. Redis** | **5a** DI + fallback | **Stretch** | Redis configurado via host/porta; falha cai para o banco | 2a |
|  | **5b** Health check + compose + testes | **Stretch** | `/health` cobre Redis; cache distribuído validado | 5a |
| **6. Cleanup** | **6a** Cleaner + índice `ExpiresAt` | **Adiado** | Apaga só expirados, em lotes, de forma idempotente | 1a |
|  | **6b** `BackgroundService` + testes | **Adiado** | Job roda no intervalo configurado e pode ser desligado em testes | 6a |
| **7. Docs** | **7a** README/AGENTS/PLAN + ADRs + screenshot | **Feito** | Docs sincronizados com o código; roadmap atualizado | todas ativas |
| **8. SQL Server** | **8a** Compose + migrations locais | **Cortado** | Provider SQL Server funciona localmente; sem deploy | 1a |

### 12.3 Ordem de execução

```text
0a → 1a → 3a → 3b → 3c → 3d → 4a → 4b → 7a
```

- **0a, 1a, 3a, 3b, 3c, 3d, 4a, 4b e 7a concluídas.** A 4b fechou em 2026-10-28 com a demo em `https://shorturl-davi.onrender.com` e a 7a fechou em 2026-10-29, encerrando o corte enxuto. O que sobra é stretch.
- **1b é opcional rápido**: fazer se o compose local ou o job Postgres da CI não atrasarem a entrega.
- **4b (deploy) acontece depois de 4a** para que a demo pública já nasça com container readiness, `DELETE` protegido e listagem.
- Não paralelizar entregas que tocam `Program.cs`, `AppDbContext` ou migrations; o custo de conflito é maior que o ganho.
- **2, 5, 6 e 3e são stretch/adiados**: só entram depois que o corte ativo estiver publicado.
- **8a está cortado**; PostgreSQL já demonstra a troca de provider.
- O Render Key Value só é necessário se o stretch de Redis for feito.

### 12.4 Critérios por entrega

- Build e testes verdes antes de cada commit.
- Commits em Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`).
- Nenhum secret versionado (`Jwt:Key`, connection strings e senhas ficam em env/secrets).
- README/AGENTS atualizados ao fim de cada entrega ativa, não apenas na Fase 7.
- Deploy só é considerado pronto após validar `/health`, Swagger, register/login, create, redirect e delete.

### 12.5 Configuração de produção

- **Neon:** usar connection string direta, sem `-pooler`, para migrations:
  ```text
  Host=...neon.tech;Database=neondb;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true;Maximum Pool Size=20;Timeout=30
  ```
- **Render:** web service Docker Free, região `virginia`, `healthCheckPath: /health/live`, `autoDeployTrigger: checksPass`. O serviço é declarado em `render.yaml` (Blueprint), não criado à mão no dashboard.
- **Secrets no Render:** `ConnectionStrings__Postgres` com `sync: false`; `Jwt__Key` com `generateValue: true`.
- **Variáveis no Render:** `Database__Provider=Postgres` e `Swagger__Enabled=true` (o Swagger fica desligado em Production por padrão). `ShortUrl__BaseUrl` é opcional; sem ela, os links usam o host público da requisição.
- **Primeiro acesso após idle:** cold start de ~1 min; não usar keep-alive, pois consumiria as 750 h/mês.
- **Stretch de Redis:** criar Render Key Value Free (`ipAllowList: []`, `persistenceMode: off`) e configurar `Redis__Host`/`Redis__Port` via `fromService`.

### 12.6 Fora do escopo ativo

- Redis, cleanup de expirados e rate limit por usuário (stretch).
- SQL Server local (cortado) e SQL Server em produção.
- Refresh tokens, roles/admin, reset de senha e verificação de e-mail.
- Frontend, analytics além de `ClickCount` e multi-tenancy.
- Escala horizontal e múltiplas instâncias no Render Free.

### 12.7 Definição de pronto do corte enxuto

- [x] PostgreSQL em produção (Neon) com migrations separadas.
- [x] Auth JWT + ownership.
- [x] Listagem paginada dos links do usuário.
- [x] Deploy público no Render Free funcionando.
- [x] README/AGENTS/PLAN sincronizados com URL, arquitetura e limitações.
- [x] CI verde com build, testes e Docker.
- [x] Swagger acessível na demo pública.
- [x] Testes cobrindo auth, ownership, create, redirect, delete e listagem paginada.

> **Corte enxuto encerrado em 2026-10-29.** Todos os itens acima estão entregues na `main`; o que resta no roadmap é **stretch** (2, 5, 6 e 3e) ou **cortado** (8a).

### 12.8 Fluxo de trabalho

- O PR #1 (`feat/roadmap-v2`) foi mergeado pelo autor.
- O desenvolvimento continua **diretamente na `main`**, sem novas branches por fase.
- Commits seguem Conventional Commits e cada entrega mantém build/testes verdes.
