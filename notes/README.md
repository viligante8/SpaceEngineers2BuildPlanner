# Notes

What was learned about the engine while building this, kept because almost none of it is written
down anywhere public. Each file records the mechanism, the evidence for it, and — where it matters —
the wrong answer that looked right first.

## Engine behaviour

| File | What it settles |
|---|---|
| [client-server-split.md](client-server-split.md) | **Read this first.** Two sessions run in-process, both have a character entity, both report the same debug name, and only one has the inventory. |
| [build-planner-api.md](build-planner-api.md) | The verified API surface: recipes, targeting, production cascade, input actions, the terminal panel. |
| [conveyor-reach.md](conveyor-reach.md) | Why `InventorySystemComponent.Inventories` is not the conveyor network, and what the engine actually walks. |

## Design and process

| File | What it covers |
|---|---|
| [build-planner-ux-spec.md](build-planner-ux-spec.md) | The SE1 behaviour being reproduced. |
| [in-game-tests.md](in-game-tests.md) | Manual test procedure. Re-run after a game update. |

## Tooling

| File | What it covers |
|---|---|
| [se2-mcp-server.md](se2-mcp-server.md) | Keen's shipped HTTP/MCP control server — investigated and **not usable**. A dead end, recorded so it is not investigated twice. |
