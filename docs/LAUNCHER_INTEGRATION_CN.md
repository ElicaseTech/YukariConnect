# 启动器集成指南

本文档说明如何将 YukariConnect 集成到你的 Minecraft 启动器中。

## 概述

YukariConnect 提供与 Terracotta 兼容的 API，用于 Minecraft LAN 隧道。启动器可以集成 YukariConnect 让玩家通过互联网托管或加入房间。

## 快速开始

### 1. 启动 YukariConnect

YukariConnect 作为后台进程运行，默认 HTTP API 端口为 5062。

```bash
# Windows
YukariConnect.exe

# Linux
./YukariConnect
```

服务启动时会记录监听端口：
```
YUKARI_PORT_INFO:port=5062
```

### 2. 检查服务状态

```http
GET http://localhost:5062/meta
```

### 3. 设置自定义 Vendor（可选）

在玩家列表中标识你的启动器：

```http
POST http://localhost:5062/config/launcher
Content-Type: application/json

{
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

vendor 字段将显示为：`YukariConnect 0.1.0 MyLauncher/1.0.0`

## 创建房间

### 方法 1：Terracotta 兼容 API

用于与现有 Terracotta 集成最大兼容性。

```http
GET http://localhost:5062/state/scanning?player=PlayerName
```

**参数：**
- `player`（可选）：玩家名称，默认为 "Host"

**响应：** `200 OK`

### 方法 2：Yukari 扩展 API

用于额外功能，如自定义 vendor 字符串。

```http
POST http://localhost:5062/room/host/start
Content-Type: application/json

{
  "scaffoldingPort": 13448,
  "playerName": "PlayerName",
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

**响应：**
```json
{
  "message": "Host starting..."
}
```

### 监控主机状态

轮询状态端点以跟踪进度：

```http
GET http://localhost:5062/state
```

**托管状态流程：**
```
waiting → host-scanning → host-starting → host-ok
```

当状态为 `host-ok` 时，检查房间代码：

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

### 获取房间代码

主机达到 `host-ok` 状态后，房间代码可用。

```http
GET http://localhost:5062/room/status
```

与其他玩家分享 `roomCode` 以便他们加入。

## 加入房间

### 方法 1：Terracotta 兼容 API

```http
GET http://localhost:5062/state/guesting?room=U/AB12-CD34-EF56-GH78&player=PlayerName
```

**参数：**
- `room`（必需）：要加入的房间代码
- `player`（可选）：玩家名称，默认为 "Guest"

**响应：** `200 OK`

### 方法 2：Yukari 扩展 API

```http
POST http://localhost:5062/room/guest/start
Content-Type: application/json

{
  "roomCode": "U/AB12-CD34-EF56-GH78",
  "playerName": "PlayerName",
  "launcherCustomString": "MyLauncher/1.0.0"
}
```

**响应：**
```json
{
  "message": "Guest joining..."
}
```

### 监控客户端状态

轮询状态端点以跟踪进度：

```http
GET http://localhost:5062/state
```

**加入状态流程：**
```
waiting → guest-connecting → guest-starting → guest-ok
```

当状态为 `guest-ok` 时，获取本地服务器地址：

```http
GET http://localhost:5062/state
```

```json
{
  "state": "guest-ok",
  "url": "127.0.0.1:25565"
}
```

### 连接到 Minecraft

使用 `url` 字段连接 Minecraft 客户端：

```
主机: 127.0.0.1
端口: 25565
```

如果 `url` 是 `127.0.0.1`（无端口），则使用默认端口 25565。

## 停止房间

```http
POST http://localhost:5062/room/stop
```

## 错误处理

### 检查错误

```http
GET http://localhost:5062/state
```

如果 `state` 是 `exception`，检查错误详情：

```http
GET http://localhost:5062/room/status
```

```json
{
  "state": "Error",
  "error": "Minecraft server offline for 6 consecutive checks"
}
```

### 从错误重试

```http
POST http://localhost:5062/room/retry
```

## Minecraft 集成

### 主机启动器

1. 启动 YukariConnect 主机模式
2. 等待 `host-ok` 状态
3. 启动 Minecraft 服务器（如果尚未运行）
4. YukariConnect 将自动检测并暴露服务器

### 客户端启动器

1. 使用房间代码启动 YukariConnect 客户端模式
2. 等待 `guest-ok` 状态
3. 从状态响应获取 `url`
4. 使用 `--server 127.0.0.1 --port 25565` 启动 Minecraft 客户端

## 玩家列表

获取当前房间中的所有玩家：

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

## 完整集成示例

### 主机流程

```python
import requests
import time

BASE_URL = "http://localhost:5062"

# 1. 设置自定义 vendor
requests.post(f"{BASE_URL}/config/launcher", json={
    "launcherCustomString": "MyLauncher/1.0.0"
})

# 2. 开始托管
response = requests.get(f"{BASE_URL}/state/scanning?player=Player1")
if response.status_code != 200:
    print("启动托管失败")
    exit(1)

# 3. 等待 host-ok 状态
while True:
    state = requests.get(f"{BASE_URL}/state").json()
    if state["state"] == "host-ok":
        break
    if state["state"] == "exception":
        print(f"错误: {state.get('error')}")
        exit(1)
    time.sleep(1)

# 4. 获取房间代码
status = requests.get(f"{BASE_URL}/room/status").json()
room_code = status["roomCode"]
print(f"房间代码: {room_code}")
print(f"与其他玩家分享此代码！")

# 5. 启动 Minecraft 服务器（你的实现）
# start_minecraft_server(...)
```

### 客户端流程

```python
import requests
import time

BASE_URL = "http://localhost:5062"
ROOM_CODE = "U/AB12-CD34-EF56-GH78"

# 1. 设置自定义 vendor
requests.post(f"{BASE_URL}/config/launcher", json={
    "launcherCustomString": "MyLauncher/1.0.0"
})

# 2. 加入房间
response = requests.get(f"{BASE_URL}/state/guesting?room={ROOM_CODE}&player=Player2")
if response.status_code != 200:
    print("加入房间失败")
    exit(1)

# 3. 等待 guest-ok 状态
while True:
    state = requests.get(f"{BASE_URL}/state").json()
    if state["state"] == "guest-ok":
        break
    if state["state"] == "exception":
        print(f"错误: {state.get('error')}")
        exit(1)
    time.sleep(1)

# 4. 获取服务器地址
server_url = state.get("url", "127.0.0.1")
print(f"连接到: {server_url}")

# 5. 启动 Minecraft 客户端（你的实现）
# start_minecraft_client(host="127.0.0.1", port=25565)
```

## 配置

YukariConnect 可以通过 `yukari.json` 配置：

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

### 配置选项

| 选项 | 类型 | 默认值 | 描述 |
|--------|------|---------|-------------|
| `HttpPort` | int | 5062 | HTTP API 端口 |
| `DefaultScaffoldingPort` | int | 13448 | Scaffolding 协议端口 |
| `LauncherCustomString` | string | null | 默认 vendor 后缀 |
| `TerracottaCompatibilityMode` | bool | true | 启动前等待 MC 服务器 |
| `McServerOfflineThreshold` | int | 6 | 错误前的离线检查次数 |
| `EasyTierStartupTimeoutSeconds` | int | 12 | 网络启动超时 |
| `CenterDiscoveryTimeoutSeconds` | int | 25 | 主机发现超时 |

## 故障排除

### 端口已被占用

如果默认端口被占用，YukariConnect 将自动查找可用端口。检查日志输出获取实际端口：

```
YUKARI_PORT_INFO:port=5063
```

### 未检测到 Minecraft 服务器

在 Terracotta 兼容模式下，主机等待 Minecraft 服务器后才转换到 `host-ok`。确保：
1. Minecraft 服务器在 LAN 上运行
2. 服务器广播到 LAN（在 server.properties 中启用）
3. 防火墙允许 UDP 端口 4445（MC LAN 广播）

### 连接超时

如果客户端连接超时：
1. 验证房间代码正确
2. 检查主机在线且处于 `host-ok` 状态
3. 增加配置中的 `CenterDiscoveryTimeoutSeconds`

## 高级：Scaffolding 协议

对于高级集成，你可以直接在端口 13448 上实现 Scaffolding TCP 协议。

有关协议详情，请参阅 [API_CN.md](API_CN.md#scaffolding-协议)。

## 支持

如有问题和疑问：
- 文档：[API_CN.md](API_CN.md)
- 协议规范：[Scaffolding-MC/Scaffolding-MC](https://github.com/Scaffolding-MC/Scaffolding-MC)
- Terracotta 兼容性：请参阅[兼容性说明](API_CN.md#兼容性说明)
