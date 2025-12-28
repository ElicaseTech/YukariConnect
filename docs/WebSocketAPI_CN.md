# YukariConnect WebSocket API 文档

## 目录

## 基础API格式
* 上行
```json
{
    "command": "string",
    "timestamp": 0,
    "data": {}
}
```
* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "string",
    "data": {}
}
```

## 状态管理命令

### 获取当前状态
* 上行
```json
{
    "command": "get_status",
    "timestamp": 0,
    "data": {}
}
```

* 下行（如果数据存在更新会自动下发）
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "get_status_response",
    "data": {
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

### 启动主机
* 上行
```json
{
    "command": "start_host",
    "timestamp": 0,
    "data": {
        "scaffoldingPort": 13448,
        "playerName": "Host",
        "launcherCustomString": "MyLauncher/1.0.0"
        "room": "optional",          // 房间代码（不提供则生成新房间）
        "player": "Host"             // 玩家名称（默认："Host"）
    }
}
```

**查询参数：**
| 参数 | 类型 | 必需 | 描述 |
|-----------|------|----------|-------------|
| `room` | string | 否 | 房间代码（不提供则生成新房间） |
| **player** | string | 否 | 玩家名称（默认："Host"） |

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "start_host_response",
    "data": {
        "room": "U/ABCD-EFGH-IJKL-MNOP", // 生成的房间代码
        "status": "ok" // 启动主机状态（"ok" 或 "error"）
    }
}
```

### 加入房间
* 上行
```json
{
    "command": "join_room",
    "timestamp": 0,
    "data": {
        "room": "U/ABCD-EFGH-IJKL-MNOP", // 房间代码
        "player": "Guest",             // 玩家名称（默认："Guest"）
        "launcherCustomString": "MyLauncher/1.0.0"
    }
}
```

**查询参数：**
| 参数 | 类型 | 必需 | 描述 |
|-----------|------|----------|-------------|
| **room** | string | 是 | 要加入的房间代码 |
| **player** | string | 否 | 玩家名称（默认："Guest"） |

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "join_room_response",
    "data": {
        "status": "ok", // 加入房间状态（"ok" 或 "error"）
    }
}
```

## 房间管理

### 获取房间状态
* 上行
```json
{
    "command": "get_room_status",
    "timestamp": 0,
    "data": {
    }
}
```

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "get_room_status_response",
    "data": {
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
}
```

### 停止房间
* 上行
```json
{
    "command": "stop_room",
    "timestamp": 0,
    "data": {
    }
}
```

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "stop_room_response",
    "data": {
        "status": "ok", // 停止房间状态（"ok" 或 "error"）
    }
}
```

### 错误重试
* 上行
```json
{
    "command": "room_retry",
    "timestamp": 0,
    "data": {
    }
}
```

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "room_retry_response",
    "data": {
        "status": "Retrying from error state...", // 错误重试状态（"ok" 或 "error"）
    }
}
```

## 配置管理

### 获取配置
* 上行
```json
{
    "command": "get_config",
    "timestamp": 0,
    "data": {
    }
}
```

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "get_config_response",
    "data": {
        "launcherCustomString": "MyLauncher/1.0.0"
    }
}
```

### 设置启动器自定义字符串
* 上行
```json
{
    "command": "set_launcher_custom_string",
    "timestamp": 0,
    "data": {
        "launcherCustomString": "MyLauncher/1.0.0"
    }
}
```

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "set_launcher_custom_string_response",
    "data": {
        "status": "Launcher custom string set to: MyLauncher/1.0.0", // 设置启动器自定义字符串状态（"ok" 或 "error"）
    }
}
```

## Minecraft LAN 发现

### 列出所有服务器（自动下发）
* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "list_servers_response",
    "data": {
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
}
```

### 通过 IP 获取服务器
* 上行
```json
{
    "command": "get_server_by_ip",
    "timestamp": 0,
    "data": {
        "ip": "192.168.1.100"
    }
}
```

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "get_server_by_ip_response",
    "data": {
        "server": {
            "endPoint": "192.168.1.100:25565",
            "motd": "我的 Minecraft 服务器",
            "isVerified": true,
            "version": "1.20.1",
            "onlinePlayers": 3,
            "maxPlayers": 20
        }
    }
}
```

### 搜索服务器
* 上行
```json
{
    "command": "search_servers",
    "timestamp": 0,
    "data": {
        "query": "Minecraft 服务器"
    }
}
```

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "search_servers_response",
    "data": {
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
}
```

### 获取 Minecraft 状态
* 上行
```json
{
    "command": "get_minecraft_status",
    "timestamp": 0,
    "data": {
    }
}
```

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "get_minecraft_status_response",
    "data": {
        "totalServers": 5,
        "verifiedServers": 3,
    }
}
```

## 网络

### 列出公共服务器（自动下发）
* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "list_public_servers_response",
    "data": {
        "servers": [
            {
            "hostname": "public1.easytier.pub",
            "port": 22016
            }
        ]
    }
}
```

## 监控

### 获取元数据
* 上行
```json
{
    "command": "get_metadata",
    "timestamp": 0,
    "data": {
    }
}
```

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "get_metadata_response",
    "data": {
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
}
```

### 获取日志（自动下发）
* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "get_log_response",
    "data": {
        "logLevel": "INFO",
        "LogType": "AspNetCore",
        "logTime": "2025-12-28T10:00:00Z",
        "logComponent": "YukariConnect",
        "logMessage": "2025-12-28 10:00:00 [INFO] 启动 EasyTier 服务器"
    }
}
```

### 紧急停止
* 上行
```json
{
    "command": "panic",
    "timestamp": 0,
    "data": {
    }
}
```

* 下行
```json
{
    "code": 0,
    "message": "string",
    "timestamp": 0,
    "command": "panic_response",
    "data": {
        "status": "Panic triggered, cleaning up..."
    }
}
```






