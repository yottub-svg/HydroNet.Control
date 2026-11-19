# HydroNet.Control

Учебный инженерный проект Александра Сковородко (S-14): диспетчерская водоканала и контроль давления в сетях.

## Реализовано

- WinForms-пульт с обнаружением `Screen.AllScreens`, переносом формы на второй монитор и единым `ScreenStateStore`.
- Снижение alarm fatigue: кластеризация каскадных тревог по первопричине, приоритеты P1–P3 и режим фокуса.
- BFF-агрегатор с параллельным `Task.WhenAll` и частичной деградацией вторичных источников.
- Break-Glass/JIT-доступ: одобрение старшим инженером, TTL ровно 15 минут, запрет self-approval и полный audit trail.
- Ядро локализации порыва по градиенту давления и минимальному набору задвижек.
- SignalR hub + reconnect coordinator с буфером и replay тревог без потерь.
- Документы внедрения, rollout, VRRP/Keepalived, firewall/GPO и MSI/MSIX-публикации.

## Проверка

```powershell
dotnet restore HydroNet.Control.sln
dotnet test HydroNet.Control.sln --configuration Release --collect:"XPlat Code Coverage"
dotnet run --project src/HydroNet.Api --configuration Release
```

Тесты покрывают градиент давления, группировку тревог, деградацию BFF, TTL JIT, аудит, два монитора, reconnect replay и правила сети.

## Структура

- `src/HydroNet.Core` — доменная модель и прикладные сервисы без UI-зависимостей.
- `src/HydroNet.Api` — health endpoint, BFF, JIT API и SignalR hub.
- `src/HydroNet.Client` — Windows Forms пульт.
- `tests/HydroNet.Tests` — unit/contract tests.
- `docs` — ТЗ, rollout, эксплуатация, сеть и публикация.
- `ops` — Nginx/Keepalived/Docker Compose конфигурация.
