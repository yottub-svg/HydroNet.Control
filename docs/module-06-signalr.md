# Модуль 06 — SignalR + REST fallback

`HydroNetHub` предоставляет подписку на район. `ReconnectCoordinator` переводит клиент в reconnecting, буферизует тревоги во время разрыва и возвращает их в порядке поступления после восстановления. REST BFF остаётся доступным как fallback для initial snapshot.
