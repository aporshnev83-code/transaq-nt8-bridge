# transaq-nt8-bridge

Practical bridge between **Transaq XML Connector** and **NinjaTrader 8** using:

- `TransaqGateway.exe` (external process with `txmlconnector64.dll` P/Invoke)
- bidirectional **Named Pipes** using JSON Lines
- a minimal NT8 AddOn window for connection, subscriptions, market data, DOM, and orders

## Solution layout

- `src/Transaq.Bridge.Core/` - shared models, envelope, JSON-line codec, queue helper.
- `src/TransaqGateway/` - connector host, callback queue + single pump thread, XML parse, pipe server.
- `src/NinjaTrader8.AddOn.TransaqBridge/` - pipe client, bridge events, WPF bridge window + NinjaScript AddOn source template.
- `tools/build-local.ps1` - Windows build and artifact packaging.
- `config/config.template.json` - gateway config template.

## Protocol (JSON Lines)

Each line is UTF-8 JSON envelope:

```json
{"type":"marketData","tsUtc":"2026-01-01T12:00:00.0000000Z","payload":{}}
```

Message types used:

- gateway -> NT: `hello`, `ping`, `marketData`, `dom`, `orderUpdate`, `positionUpdate`, `log`, `error`
- NT -> gateway: `pong`, `subscribe`, `newOrder`, `cancelOrder`

## Gateway run

1. Copy config:
   - `copy config\config.template.json config.json`
2. Fill `Login`, `Password`, `Host`, `Port`, and optional defaults.
3. Put `txmlconnector64.dll` near `TransaqGateway.exe` (or in `%PATH%`).
4. Run:
   - `TransaqGateway.exe config.json`

Gateway behavior:

- callback thread only enqueues raw XML text
- single pump thread parses XML and updates in-memory state
- publishes snapshots/events to all connected pipe clients
- maps `clientOrderId -> transactionId` for cancel requests

## NinjaTrader AddOn install

1. Build artifacts: `powershell -ExecutionPolicy Bypass -File .\tools\build-local.ps1`
2. Use `dist\NT8_AddOn_Source.zip` for NT import:
   - NinjaTrader: **Tools -> Import -> NinjaScript Add-On**
3. Open UI from **New -> Transaq Bridge**.
4. In the window, set `Pipe`, optionally set `Gateway EXE` and click **Launch Gateway**.
5. Click **Connect**, then subscribe with `Board` + `Seccode`.

## Trading commands

- `Market Buy/Sell` => `<neworder><bymarket>true</bymarket>`
- `Limit Buy/Sell` => `<neworder><price>..</price>`
- `Cancel Last` => `<cancelorder>` by transaction id mapped from client order id

## Logging

- Gateway file log: `logs/gateway.log`
- AddOn logs through debug/output messages.

## Notes

- Target framework is **.NET Framework 4.8** (`net48`), C# 7.3-safe style.
- No full NT8 market data provider implementation; this is a practical bridge with events:
  - `BridgeEvents.OnMarketData`
  - `BridgeEvents.OnDom`
  - `BridgeEvents.OnOrderUpdate`
- Architecture is ready to add an External Data Feed adapter later.
