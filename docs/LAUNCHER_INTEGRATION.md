# Launcher Integration Guide

This guide explains how to integrate YukariConnect into your Minecraft launcher.

## Overview

YukariConnect provides a Terracotta-compatible API for Minecraft LAN tunneling. Launchers can integrate YukariConnect to enable players to host or join rooms over the internet.

## Quick Start

### 1. Start YukariConnect

YukariConnect runs as a background process. The HTTP API is available on port 5062 by default.

```bash
# Windows
YukariConnect.exe

# Linux
./YukariConnect
```

The service logs its listening port on startup:
```
YUKARI_PORT_INFO:port=5062
```

### 2. Check Service Status

```http
GET http://localhost:5062/meta
```

### 3. Set Custom Vendor (Optional)

Identify your launcher in the player list:

```http
POST http://localhost:5062/config/launcher
Content-Type: application/json

{
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

The vendor field will appear as: `YukariConnect 0.1.0 MyLauncher/1.0.0`

## Hosting a Room

### Method 1: Terracotta Compatible API

Use this for maximum compatibility with existing Terracotta integrations.

```http
GET http://localhost:5062/state/scanning?player=PlayerName
```

**Parameters:**
- `player` (optional): Player name, default is "Host"

**Response:** `200 OK`

### Method 2: Yukari Extended API

Use this for additional features like custom vendor strings.

```http
POST http://localhost:5062/room/host/start
Content-Type: application/json

{
  "scaffoldingPort": 13448,
  "playerName": "PlayerName",
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

**Response:**
```json
{
  "message": "Host starting..."
}
```

### Monitoring Host State

Poll the state endpoint to track progress:

```http
GET http://localhost:5062/state
```

**State Flow for Hosting:**
```
waiting → host-scanning → host-starting → host-ok
```

When state is `host-ok`, check for the room code:

```http
GET http://localhost:5062/room/status
```

```json
{
  "roomCode": "U/AB12-CD34-EF56-GH78",
  "minecraftPort": 25565,
  "players": [...]
}
```

### Getting the Room Code

The room code is available after the host reaches `host-ok` state.

```http
GET http://localhost:5062/room/status
```

Share the `roomCode` with other players so they can join.

## Joining a Room

### Method 1: Terracotta Compatible API

```http
GET http://localhost:5062/state/guesting?room=U/AB12-CD34-EF56-GH78&player=PlayerName
```

**Parameters:**
- `room` (required): Room code to join
- `player` (optional): Player name, default is "Guest"

**Response:** `200 OK`

### Method 2: Yukari Extended API

```http
POST http://localhost:5062/room/guest/start
Content-Type: application/json

{
  "roomCode": "U/AB12-CD34-EF56-GH78",
  "playerName": "PlayerName",
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

**Response:**
```json
{
  "message": "Guest joining..."
}
```

### Monitoring Guest State

Poll the state endpoint to track progress:

```http
GET http://localhost:5062/state
```

**State Flow for Joining:**
```
waiting → guest-connecting → guest-starting → guest-ok
```

When state is `guest-ok`, get the localhost server address:

```http
GET http://localhost:5062/state
```

```json
{
  "state": "guest-ok",
  "url": "127.0.0.1:25565"
}
```

### Connecting to Minecraft

Use the `url` field to connect the Minecraft client:

```
Host: 127.0.0.1
Port: 25565
```

If `url` is `127.0.0.1` (no port), the default port 25565 is used.

## Stopping a Room

```http
POST http://localhost:5062/room/stop
```

## Error Handling

### Check for Errors

```http
GET http://localhost:5062/state
```

If `state` is `exception`, check the error details:

```http
GET http://localhost:5062/room/status
```

```json
{
  "state": "Error",
  "error": "Minecraft server offline for 6 consecutive checks"
}
```

### Retry from Error

```http
POST http://localhost:5062/room/retry
```

## Minecraft Integration

### For Host Launchers

1. Start YukariConnect host mode
2. Wait for `host-ok` state
3. Start the Minecraft server (if not already running)
4. YukariConnect will automatically detect and expose the server

### For Guest Launchers

1. Start YukariConnect guest mode with room code
2. Wait for `guest-ok` state
3. Get the `url` from state response
4. Launch Minecraft client with `--server 127.0.0.1 --port 25565`

## Player List

Get all players in the current room:

```http
GET http://localhost:5062/state
```

```json
{
  "profiles": [
    {
      "name": "Player1",
      "machineId": "abc123...",
      "vendor": "YukariConnect 0.1.0 MyLauncher/1.0.0",
      "kind": "HOST"
    },
    {
      "name": "Player2",
      "machineId": "def456...",
      "vendor": "YukariConnect 0.1.0",
      "kind": "GUEST"
    }
  ]
}
```

## Complete Integration Example

### Host Flow

```python
import requests
import time

BASE_URL = "http://localhost:5062"

# 1. Set custom vendor
requests.post(f"{BASE_URL}/config/launcher", json={
    "launcherCustomString": "MyLauncher/1.0.0"
})

# 2. Start hosting
response = requests.get(f"{BASE_URL}/state/scanning?player=Player1")
if response.status_code != 200:
    print("Failed to start hosting")
    exit(1)

# 3. Wait for host-ok state
while True:
    state = requests.get(f"{BASE_URL}/state").json()
    if state["state"] == "host-ok":
        break
    if state["state"] == "exception":
        print(f"Error: {state.get('error')}")
        exit(1)
    time.sleep(1)

# 4. Get room code
status = requests.get(f"{BASE_URL}/room/status").json()
room_code = status["roomCode"]
print(f"Room code: {room_code}")
print(f"Share this code with other players!")

# 5. Start Minecraft server (your implementation)
# start_minecraft_server(...)
```

### Guest Flow

```python
import requests
import time

BASE_URL = "http://localhost:5062"
ROOM_CODE = "U/AB12-CD34-EF56-GH78"

# 1. Set custom vendor
requests.post(f"{BASE_URL}/config/launcher", json={
    "launcherCustomString": "MyLauncher/1.0.0"
})

# 2. Join room
response = requests.get(f"{BASE_URL}/state/guesting?room={ROOM_CODE}&player=Player2")
if response.status_code != 200:
    print("Failed to join room")
    exit(1)

# 3. Wait for guest-ok state
while True:
    state = requests.get(f"{BASE_URL}/state").json()
    if state["state"] == "guest-ok":
        break
    if state["state"] == "exception":
        print(f"Error: {state.get('error')}")
        exit(1)
    time.sleep(1)

# 4. Get server address
server_url = state.get("url", "127.0.0.1")
print(f"Connect to: {server_url}")

# 5. Start Minecraft client (your implementation)
# start_minecraft_client(host="127.0.0.1", port=25565)
```

## Configuration

YukariConnect can be configured via `yukari.json`:

```json
{
  "HttpPort": 5062,
  "DefaultScaffoldingPort": 13448,
  "LauncherCustomString": "MyLauncher/1.0.0",
  "TerracottaCompatibilityMode": true,
  "McServerOfflineThreshold": 6,
  "EasyTierStartupTimeoutSeconds": 12,
  "CenterDiscoveryTimeoutSeconds": 25
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `HttpPort` | int | 5062 | HTTP API port |
| `DefaultScaffoldingPort` | int | 13448 | Scaffolding protocol port |
| `LauncherCustomString` | string | null | Default vendor suffix |
| `TerracottaCompatibilityMode` | bool | true | Wait for MC server before starting |
| `McServerOfflineThreshold` | int | 6 | Offline checks before error |
| `EasyTierStartupTimeoutSeconds` | int | 12 | Network startup timeout |
| `CenterDiscoveryTimeoutSeconds` | int | 25 | Host discovery timeout |

## Troubleshooting

### Port Already in Use

If the default port is occupied, YukariConnect will automatically find an available port. Check the log output for the actual port:

```
YUKARI_PORT_INFO:port=5063
```

### Minecraft Server Not Detected

In Terracotta compatibility mode, the host waits for a Minecraft server before transitioning to `host-ok`. Ensure:
1. Minecraft server is running on the LAN
2. The server broadcasts to LAN (enable in server.properties)
3. Firewall allows UDP port 4445 (MC LAN broadcast)

### Connection Timeout

If guest connection times out:
1. Verify the room code is correct
2. Check host is online and in `host-ok` state
3. Increase `CenterDiscoveryTimeoutSeconds` in config

## Advanced: Scaffolding Protocol

For advanced integrations, you can implement the Scaffolding TCP protocol directly on port 13448.

See [API.md](API.md#scaffolding-protocol) for protocol details.

## Support

For issues and questions:
- Documentation: [API.md](API.md)
- Protocol spec: [Scaffolding-MC/Scaffolding-MC](https://github.com/Scaffolding-MC/Scaffolding-MC)
- Terracotta compatibility: See [Compatibility Notes](API.md#compatibility-notes)
