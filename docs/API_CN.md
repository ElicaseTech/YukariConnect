# YukariConnect API 文档

YukariConnect 提供与 Terracotta 兼容的 RESTful API，以及扩展功能。

## 目录

- [基础 URL](#基础-url)
- [认证](#认证)
- [响应格式](#响应格式)
- [状态管理](#状态管理)
- [房间管理](#房间管理)
- [配置管理](#配置管理)
- [Minecraft LAN 发现](#minecraft-lan-发现)
- [网络](#网络)
- [监控](#监控)

---

## 基础 URL

```
http://localhost:5062
```

默认端口为 `5062`，可在 `yukari.json` 中配置。

---

## 认证

所有端点无需认证。

---

## 响应格式

### 成功响应
```json
{
  "field": "value"
}
```

### 错误响应
```json
{
  "error": "错误消息描述"
}
```

---

## 状态管理

### 获取当前状态

获取当前应用状态和房间信息。

```http
GET /state
```

**响应：**
```json
{
  "state": "waiting",           // 当前状态（见下方状态值表）
  "role": null,                 // "host" 或 "guest"（如果在房间中）
  "room": null,                 // 房间代码（如果在房间中）
  "profileIndex": 0,            // 玩家索引
  "profiles": [                 // 房间内的玩家列表
    {
      "name": "Player1",
      "machineId": "abc123...",
      "vendor": "YukariConnect 0.1.0",
      "kind": "HOST"
    }
  ],
  "url": null,                  // MC 服务器地址（仅 guest 模式）
  "difficulty": null            // 连接难度（如适用）
}
```

**状态值：**
| 值 | 描述 |
|-------|-------------|
| `waiting` | 空闲，等待操作 |
| `host-scanning` | 主机：正在扫描 Minecraft 服务器 |
| `host-starting` | 主机：正在启动网络服务 |
| `host-ok` | 主机：运行成功 |
| `guest-connecting` | 客户端：正在连接房间 |
| `guest-starting` | 客户端：正在启动网络服务 |
| `guest-ok` | 客户端：连接成功 |
| `exception` | 发生错误 |

---

### 启动主机（Terracotta 兼容）

启动房间主机。Terracotta 兼容端点。

```http
GET /state/scanning?room=optional&player=PlayerName
```

**查询参数：**
| 参数 | 类型 | 必需 | 描述 |
|-----------|------|----------|-------------|
| `room` | string | 否 | 房间代码（不提供则生成新房间） |
| **player** | string | 否 | 玩家名称（默认："Host"） |

**响应：** `200 OK`

**注意：** 调用此端点前，使用 `/config/launcher` 设置自定义 vendor 字符串。

---

### 加入房间（Terracotta 兼容）

以客人身份加入现有房间。Terracotta 兼容端点。

```http
GET /state/guesting?room=U/ABCD-EFGH-IJKL-MNOP&player=PlayerName
```

**查询参数：**
| 参数 | 类型 | 必需 | 描述 |
|-----------|------|----------|-------------|
| **room** | string | 是 | 要加入的房间代码 |
| **player** | string | 否 | 玩家名称（默认："Guest"） |

**响应：** `200 OK` 或 `400 Bad Request`

**注意：** 调用此端点前，使用 `/config/launcher` 设置自定义 vendor 字符串。

---

## 房间管理

### 获取房间状态

获取详细房间状态（Yukari 扩展端点）。

```http
GET /room/status
```

**响应：**
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

### 启动主机（Yukari 扩展）

使用完整选项启动主机。Yukari 扩展端点。

```http
POST /room/host/start
Content-Type: application/json

{
  "scaffoldingPort": 13448,
  "playerName": "Host",
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

**请求体：**
| 字段 | 类型 | 必需 | 描述 |
|-------|------|----------|-------------|
| `scaffoldingPort` | number | 否 | Scaffolding 服务器端口（默认：13448） |
| `playerName` | string | 否 | 玩家名称（默认："Host"） |
| `launcherCustomString` | string | 否 | 自定义 vendor 后缀字符串 |

**响应：**
```json
{
  "message": "Host starting..."
}
```

---

### 启动客户端（Yukari 扩展）

使用完整选项加入房间。Yukari 扩展端点。

```http
POST /room/guest/start
Content-Type: application/json

{
  "roomCode": "U/ABCD-EFGH-IJKL-MNOP",
  "playerName": "Guest",
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

**请求体：**
| 字段 | 类型 | 必需 | 描述 |
|-------|------|----------|-------------|
| **roomCode** | string | 是 | 要加入的房间代码 |
| `playerName` | string | 否 | 玩家名称（默认："Guest"） |
| `launcherCustomString` | string | 否 | 自定义 vendor 后缀字符串 |

**响应：**
```json
{
  "message": "Guest joining..."
}
```

---

### 停止房间

停止当前房间操作。

```http
POST /room/stop
```

**响应：**
```json
{
  "message": "Room stopped"
}
```

---

### 错误重试

从错误状态重试操作。

```http
POST /room/retry
```

**响应：**
```json
{
  "message": "Retrying from error state..."
}
```

---

## 配置管理

### 获取配置

获取当前配置值。

```http
GET /config
```

**响应：**
```json
{
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

---

### 设置启动器自定义字符串

设置自定义启动器字符串用于 vendor 标识。

```http
POST /config/launcher
Content-Type: application/json

{
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

**请求体：**
| 字段 | 类型 | 必需 | 描述 |
|-------|------|----------|-------------|
| `launcherCustomString` | string | 否 | 自定义字符串（设为 null 可清除） |

**响应：**
```json
{
  "message": "Launcher custom string set to: MyLauncher/1.0.0"
}
```

**使用示例：**
```bash
# 在启动主机前设置自定义 vendor
curl -X POST http://localhost:5062/config/launcher \
  -H "Content-Type: application/json" \
  -d '{"launcherCustomString": "PCL2 1.0.0"}'

# 现在启动主机（vendor 将为 "YukariConnect 0.1.0 PCL2 1.0.0"）
curl "http://localhost:5062/state/scanning?player=Player1"
```

---

## Minecraft LAN 发现

### 列出所有服务器

列出所有发现的 Minecraft LAN 服务器。

```http
GET /minecraft/servers
```

**响应：**
```json
{
  "servers": [
    {
      "endPoint": "192.168.1.100:25565",
      "motd": "我的 Minecraft 服务器",
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

### 列出已验证服务器

仅列出已验证（可响应）的服务器。

```http
GET /minecraft/servers/verified
```

**响应：** 与 `/minecraft/servers` 相同

---

### 通过 IP 获取服务器

通过 IP 地址获取特定服务器。

```http
GET /minecraft/servers/{ip}
```

**URL 参数：**
| 参数 | 类型 | 描述 |
|-----------|------|-------------|
| **ip** | string | 服务器 IP 地址 |

**响应：**
```json
{
  "endPoint": "192.168.1.100:25565",
  "motd": "我的 Minecraft 服务器",
  "isVerified": true,
  "version": "1.20.1",
  "onlinePlayers": 3,
  "maxPlayers": 20
}
```

---

### 搜索服务器

按 MOTD 模式搜索服务器。

```http
GET /minecraft/servers/search?pattern=MyServer
```

**查询参数：**
| 参数 | 类型 | 必需 | 描述 |
|-----------|------|----------|-------------|
| `pattern` | string | 否 | MOTD 搜索模式（为空则返回全部） |

---

### 获取 Minecraft 状态

获取 Minecraft LAN 发现状态。

```http
GET /minecraft/status
```

**响应：**
```json
{
  "totalServers": 5,
  "verifiedServers": 3,
  "timestamp": "2025-12-28T10:30:00Z"
}
```

---

## 网络

### 列出公共服务器

列出可用的公共 EasyTier 服务器。

```http
GET /easytier/servers
```

**响应：**
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

## 监控

### 获取元数据

获取应用元数据和版本信息。

```http
GET /meta
```

**响应：**
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

### 获取日志（SSE）

通过服务器发送事件订阅服务器日志。

```http
GET /log
```

**响应：** `text/event-stream` 流，包含日志事件。

---

### 紧急停止

立即停止所有操作并清理资源。

```http
POST /panic
```

**响应：**
```json
{
  "message": "Panic triggered, cleaning up..."
}
```

---

### IDE 状态

获取 IDE 集成用的状态信息。

```http
GET /state/ide
```

**响应：** 与 `/state` 格式相同，包含额外的 IDE 特定字段。

---

## 房间代码格式

房间代码遵循格式：`U/NNNN-NNNN-SSSS-SSSS`

- **前缀：** `U/`（Universal）
- **网络 ID：** `NNNN-NNNN`（8 个字符，base34）
- **密钥：** `SSSS-SSSS`（8 个字符，base34）

**示例：** `U/AB12-CD34-EF56-GH78`

---

## Scaffolding 协议

YukariConnect 在端口 13448 上实现了与 Terracotta 兼容的 Scaffolding TCP 协议。

### 协议命令

| 命令 | 描述 |
|---------|-------------|
| `c:ping` | 回显请求，用于连通性测试 |
| `c:protocols` | 列出支持的协议 |
| `c:server_port` | 获取 Minecraft 服务器端口 |
| `c:player_ping` | 注册/更新玩家心跳 |
| `c:player_profiles_list` | 获取所有已连接玩家 |

### 协议格式

**请求：**
```
[1 字节: kind 长度][kind: UTF-8][4 字节: body 长度 (BE)][body: 字节]
```

**响应：**
```
[1 字节: 状态][4 字节: data 长度 (BE)][data: 字节]
```

有关协议详情，请参阅 [Scaffolding 文档](https://github.com/Scaffolding-MC/Scaffolding-MC)。

---

## 错误代码

| HTTP 状态 | 含义 |
|-------------|---------|
| `200 OK` | 成功 |
| `400 Bad Request` | 参数无效或房间代码错误 |
| `404 Not Found` | 资源未找到 |
| `500 Internal Server Error` | 服务器错误 |

---

## 兼容性说明

### Terracotta 兼容端点

这些端点与 Terracotta 客户端完全兼容：
- `GET /state`
- `GET /state/scanning`
- `GET /state/guesting`
- `GET /meta`
- `GET /log`
- `POST /panic`

### Yukari 扩展端点

这些端点提供额外功能：
- `GET /room/status`
- `POST /room/host/start`
- `POST /room/guest/start`
- `POST /room/stop`
- `POST /room/retry`
- `GET /config`
- `POST /config/launcher`
- `GET /minecraft/*`
- `GET /easytier/servers`

### Vendor 自定义

Terracotta 不支持通过 API 参数自定义 vendor。YukariConnect 通过以下方式提供此功能：

1. **配置文件：** 在 `yukari.json` 中设置 `LauncherCustomString`
2. **Yukari API：** 在 `/room/host/start` 或 `/room/guest/start` 中使用 `launcherCustomString`
3. **运行时 API：** 在启动房间前使用 `POST /config/launcher`

为保证 Terracotta 兼容性，在调用 `/state/*` 端点前使用选项 3。

---

## 快速开始

### 作为主机启动

```bash
# 方法 1：使用 Terracotta 兼容 API
curl "http://localhost:5062/state/scanning?player=MyName"

# 方法 2：使用 Yukari 扩展 API（支持自定义 vendor）
curl -X POST http://localhost:5062/room/host/start \
  -H "Content-Type: application/json" \
  -d '{"playerName": "MyName", "launcherCustomString": "PCL2 1.0.0"}'
```

### 加入房间

```bash
# 方法 1：使用 Terracotta 兼容 API
curl "http://localhost:5062/state/guesting?room=U/ABCD-EFGH-IJKL-MNOP&player=MyName"

# 方法 2：使用 Yukari 扩展 API（支持自定义 vendor）
curl -X POST http://localhost:5062/room/guest/start \
  -H "Content-Type: application/json" \
  -d '{"roomCode": "U/ABCD-EFGH-IJKL-MNOP", "playerName": "MyName", "launcherCustomString": "PCL2 1.0.0"}'
```

### 自定义 Vendor 标识

```bash
# 为 Terracotta 客户端设置自定义 vendor
curl -X POST http://localhost:5062/config/launcher \
  -H "Content-Type: application/json" \
  -d '{"launcherCustomString": "MyLauncher/1.0.0"}'

# 现在启动房间，vendor 将为 "YukariConnect 0.1.0 MyLauncher/1.0.0"
```
