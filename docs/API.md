# YukariConnect API Documentation

YukariConnect provides a RESTful API compatible with Terracotta, along with extended functionality for enhanced features.

## Table of Contents

- [Base URL](#base-url)
- [Authentication](#authentication)
- [Response Format](#response-format)
- [State Management](#state-management)
- [Room Management](#room-management)
- [Configuration](#configuration)
- [Minecraft LAN Discovery](#minecraft-lan-discovery)
- [Network](#network)
- [Monitoring](#monitoring)

---

## Base URL

```
http://localhost:5062
```

The default port is `5062` but can be configured in `yukari.json`.

---

## Authentication

No authentication is required for any endpoint.

---

## Response Format

### Success Response
```json
{
  "field": "value"
}
```

### Error Response
```json
{
  "error": "Error message description"
}
```

---

## State Management

### Get Current State

Get the current application state and room information.

```http
GET /state
```

**Response:**
```json
{
  "state": "waiting",           // Current state (see State Values below)
  "role": null,                 // "host" or "guest" if in a room
  "room": null,                 // Room code if in a room
  "profileIndex": 0,            // Profile index
  "profiles": [                 // List of players in the room
    {
      "name": "Player1",
      "machineId": "abc123...",
      "vendor": "YukariConnect 0.1.0",
      "kind": "HOST"
    }
  ],
  "url": null,                  // MC server URL (guest mode only)
  "difficulty": null            // Connection difficulty (if applicable)
}
```

**State Values:**
| Value | Description |
|-------|-------------|
| `waiting` | Idle, waiting for operation |
| `host-scanning` | Host: Scanning for Minecraft server |
| `host-starting` | Host: Starting network services |
| `host-ok` | Host: Running successfully |
| `guest-connecting` | Guest: Connecting to room |
| `guest-starting` | Guest: Starting network services |
| `guest-ok` | Guest: Connected successfully |
| `exception` | Error occurred |

---

### Start Host (Terracotta Compatible)

Start hosting a room. Terracotta compatible endpoint.

```http
GET /state/scanning?room=optional&player=PlayerName
```

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `room` | string | No | Room code (generates new if not provided) |
| **player** | string | No | Player name (default: "Host") |

**Response:** `200 OK`

**Note:** Use `/config/launcher` to set custom vendor string before calling this endpoint.

---

### Join Room (Terracotta Compatible)

Join an existing room as a guest. Terracotta compatible endpoint.

```http
GET /state/guesting?room=U/ABCD-EFGH-IJKL-MNOP&player=PlayerName
```

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| **room** | string | Yes | Room code to join |
| **player** | string | No | Player name (default: "Guest") |

**Response:** `200 OK` or `400 Bad Request`

**Note:** Use `/config/launcher` to set custom vendor string before calling this endpoint.

---

## Room Management

### Get Room Status

Get detailed room status (Yukari extended endpoint).

```http
GET /room/status
```

**Response:**
```json
{
  "state": "Host_Running",
  "role": "host",
  "error": null,
  "roomCode": "U/ABCD-EFGH-IJKL-MNOP",
  "players": [
    {
      "name": "Player1",
      "machineId": "abc123...",
      "vendor": "YukariConnect 0.1.0",
      "kind": "HOST"
    }
  ],
  "minecraftPort": 25565,
  "lastUpdate": "2025-12-28T10:30:00Z"
}
```

---

### Start Host (Yukari Extended)

Start hosting with full options. Yukari extended endpoint.

```http
POST /room/host/start
Content-Type: application/json

{
  "scaffoldingPort": 13448,
  "playerName": "Host",
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

**Request Body:**
| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `scaffoldingPort` | number | No | Scaffolding server port (default: 13448) |
| `playerName` | string | No | Player name (default: "Host") |
| `launcherCustomString` | string | No | Custom vendor string suffix |

**Response:**
```json
{
  "message": "Host starting..."
}
```

---

### Start Guest (Yukari Extended)

Join a room with full options. Yukari extended endpoint.

```http
POST /room/guest/start
Content-Type: application/json

{
  "roomCode": "U/ABCD-EFGH-IJKL-MNOP",
  "playerName": "Guest",
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

**Request Body:**
| Field | Type | Required | Description |
|-------|------|----------|-------------|
| **roomCode** | string | Yes | Room code to join |
| `playerName` | string | No | Player name (default: "Guest") |
| `launcherCustomString` | string | No | Custom vendor string suffix |

**Response:**
```json
{
  "message": "Guest joining..."
}
```

---

### Stop Room

Stop the current room operation.

```http
POST /room/stop
```

**Response:**
```json
{
  "message": "Room stopped"
}
```

---

### Retry from Error

Retry operation from an error state.

```http
POST /room/retry
```

**Response:**
```json
{
  "message": "Retrying from error state..."
}
```

---

## Configuration

### Get Configuration

Get current configuration values.

```http
GET /config
```

**Response:**
```json
{
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

---

### Set Launcher Custom String

Set the custom launcher string for vendor identification.

```http
POST /config/launcher
Content-Type: application/json

{
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

**Request Body:**
| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `launcherCustomString` | string | No | Custom string (null to clear) |

**Response:**
```json
{
  "message": "Launcher custom string set to: MyLauncher/1.0.0"
}
```

**Usage Example:**
```bash
# Set custom vendor before starting host
curl -X POST http://localhost:5062/config/launcher \
  -H "Content-Type: application/json" \
  -d '{"launcherCustomString": "PCL2 1.0.0"}'

# Now start host (vendor will be "YukariConnect 0.1.0 PCL2 1.0.0")
curl "http://localhost:5062/state/scanning?player=Player1"
```

---

## Minecraft LAN Discovery

### List All Servers

List all discovered Minecraft LAN servers.

```http
GET /minecraft/servers
```

**Response:**
```json
{
  "servers": [
    {
      "endPoint": "192.168.1.100:25565",
      "motd": "My Minecraft Server",
      "isVerified": true,
      "version": "1.20.1",
      "onlinePlayers": 3,
      "maxPlayers": 20
    }
  ],
  "count": 1
}
```

---

### List Verified Servers

List only verified (responsive) servers.

```http
GET /minecraft/servers/verified
```

**Response:** Same as `/minecraft/servers`

---

### Get Server by IP

Get a specific server by IP address.

```http
GET /minecraft/servers/{ip}
```

**URL Parameter:**
| Parameter | Type | Description |
|-----------|------|-------------|
| **ip** | string | Server IP address |

**Response:**
```json
{
  "endPoint": "192.168.1.100:25565",
  "motd": "My Minecraft Server",
  "isVerified": true,
  "version": "1.20.1",
  "onlinePlayers": 3,
  "maxPlayers": 20
}
```

---

### Search Servers

Search servers by MOTD pattern.

```http
GET /minecraft/servers/search?pattern=MyServer
```

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `pattern` | string | No | MOTD search pattern (returns all if empty) |

---

### Get Minecraft Status

Get Minecraft LAN discovery status.

```http
GET /minecraft/status
```

**Response:**
```json
{
  "totalServers": 5,
  "verifiedServers": 3,
  "timestamp": "2025-12-28T10:30:00Z"
}
```

---

## Network

### List Public Servers

List available public EasyTier servers.

```http
GET /easytier/servers
```

**Response:**
```json
{
  "servers": [
    {
      "hostname": "public1.easytier.pub",
      "port": 22016
    }
  ]
}
```

---

## Monitoring

### Get Metadata

Get application metadata and version information.

```http
GET /meta
```

**Response:**
```json
{
  "version": "1.0.0",
  "compileTimestamp": "2025-12-28T10:00:00Z",
  "easyTierVersion": "0.10.0",
  "yggdrasilPort": "13448",
  "targetTuple": "X64-X64-Windows",
  "targetArch": "X64",
  "targetVendor": "X64",
  "targetOS": "Microsoft Windows 10.0.26200",
  "targetEnv": ".NET 8.0.0"
}
```

---

### Get Logs (SSE)

Subscribe to server logs via Server-Sent Events.

```http
GET /log
```

**Response:** `text/event-stream` stream with log events.

---

### Panic (Emergency Stop)

Immediately stop all operations and clean up.

```http
POST /panic
```

**Response:**
```json
{
  "message": "Panic triggered, cleaning up..."
}
```

---

### IDE State

Get state information for IDE integration.

```http
GET /state/ide
```

**Response:** Same format as `/state` with additional IDE-specific fields.

---

## Room Code Format

Room codes follow the format: `U/NNNN-NNNN-SSSS-SSSS`

- **Prefix:** `U/` (Universal)
- **Network ID:** `NNNN-NNNN` (8 characters, base34)
- **Secret:** `SSSS-SSSS` (8 characters, base34)

**Example:** `U/AB12-CD34-EF56-GH78`

---

## Scaffolding Protocol

YukariConnect implements the Terracotta-compatible Scaffolding TCP protocol on port 13448.

### Protocol Commands

| Command | Description |
|---------|-------------|
| `c:ping` | Echo request for connectivity test |
| `c:protocols` | List supported protocols |
| `c:server_port` | Get Minecraft server port |
| `c:player_ping` | Register/update player heartbeat |
| `c:player_profiles_list` | Get all connected players |

### Protocol Format

**Request:**
```
[1 byte: kind length][kind: UTF-8][4 bytes: body length (BE)][body: bytes]
```

**Response:**
```
[1 byte: status][4 bytes: data length (BE)][data: bytes]
```

For protocol details, see the [Scaffolding documentation](https://github.com/Scaffolding-MC/Scaffolding-MC).

---

## Error Codes

| HTTP Status | Meaning |
|-------------|---------|
| `200 OK` | Success |
| `400 Bad Request` | Invalid parameters or room code |
| `404 Not Found` | Resource not found |
| `500 Internal Server Error` | Server error |

---

## Compatibility Notes

### Terracotta Compatible Endpoints

These endpoints are fully compatible with Terracotta clients:
- `GET /state`
- `GET /state/scanning`
- `GET /state/guesting`
- `GET /meta`
- `GET /log`
- `POST /panic`

### Yukari Extended Endpoints

These endpoints provide additional functionality:
- `GET /room/status`
- `POST /room/host/start`
- `POST /room/guest/start`
- `POST /room/stop`
- `POST /room/retry`
- `GET /config`
- `POST /config/launcher`
- `GET /minecraft/*`
- `GET /easytier/servers`

### Vendor Customization

Terracotta does not support vendor customization via API parameters. YukariConnect provides this through:

1. **Configuration file:** Set `LauncherCustomString` in `yukari.json`
2. **Yukari API:** Use `launcherCustomString` in `/room/host/start` or `/room/guest/start`
3. **Runtime API:** Use `POST /config/launcher` before starting a room

For Terracotta compatibility, use option 3 before calling `/state/*` endpoints.
