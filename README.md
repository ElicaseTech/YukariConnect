# YukariConnect

> A Minecraft LAN virtual networking solution using P2P mesh networks, compatible with the Scaffolding protocol.

## Features

- **P2P Virtual Networking**: Create virtual mesh networks for Minecraft LAN gameplay over the internet
- **Scaffolding Protocol**: Compatible with the Scaffolding protocol for room management and player discovery
- **Host & Guest Modes**:
  - **HostCenter**: Host a Minecraft server and invite players via room codes
  - **Guest**: Join rooms using room codes to play remotely
- **Network Abstraction Layer**: Clean separation between application logic and network implementation
- **Dynamic Port Management**: Automatic whitelist and port forwarding configuration
- **Minecraft FakeServer**: Broadcast remote Minecraft servers to local LAN for seamless integration

## Architecture

### Network Layer

YukariConnect uses a network abstraction layer to decouple the application from the underlying P2P implementation:

```
YukariConnect.Network
├── INetworkNode           - Node operations (peers, forwarding, firewall)
├── IPeerDiscoveryService  - Public server discovery
├── INetworkProcess        - Process lifecycle management
└── EasyTier*              - EasyTier implementation
```

### Scaffolding Protocol

The Scaffolding protocol enables:
- Room code generation/validation (format: `U/NNNN-NNNN-SSSS-SSSS`)
- Player registration and heartbeat
- Minecraft server port discovery
- Player list management

### State Machine

```
Host Flow:
  Idle → Host_Prepare → Host_EasyTierStarting → Host_MinecraftDetecting → Host_Running

Guest Flow:
  Idle → Guest_Prepare → Guest_EasyTierStarting → Guest_DiscoveringCenter
  → Guest_ConnectingScaffolding → Guest_Running
```

## Building

```bash
dotnet build
```

## Running

```bash
dotnet run
```

The API will be available at `http://localhost:5000`.

## API Endpoints

### Meta
- `GET /` - Service metadata

### State
- `GET /state` - Current state and capabilities

### Room
- `POST /room/host` - Start hosting a room
- `POST /room/guest` - Join a room as guest
- `DELETE /room` - Leave current room
- `POST /room/retry` - Retry after error

### Minecraft
- `GET /mc/servers` - List discovered Minecraft servers
- `GET /mc/status` - Current Minecraft connection status

### EasyTier
- `GET /easytier/version` - EasyTier version
- `GET /easytier/public-servers` - Available public servers

## Configuration

Resources are loaded from the `resource/` directory:
- `easytier-core` / `easytier-core.exe` - P2P network daemon
- `easytier-cli` / `easytier-cli.exe` - Control CLI
- `machine_id.txt` - Persistent machine identifier (auto-generated)

## Network Topology

```
                    Internet
                       |
                 [Public Servers]
                  /     |     \
                 /      |      \
           [Host]  ---- P2P Mesh ----  [Guest 1]
           10.144.144.1               10.144.144.2
                |
           [Guest 2]
           10.144.144.3
```

## Room Code Format

Room codes use the format `U/NNNN-NNNN-SSSS-SSSS`:
- `U/` - Protocol prefix
- `NNNN-NNNN` - Network name identifier (checksum: divisible by 7)
- `SSSS-SSSS` - Network secret (checksum: divisible by 7)

Character set: `0-9A-HJ-NP-Z` (no I, O to avoid confusion)

## Compatibility

- **Protocol**: Scaffolding (Terracotta-compatible)
- **Network**: EasyTier (with `--no-tun` mode)
- **Minecraft**: Java Edition LAN worlds
- **Platform**: Windows, Linux, macOS

## License

This project is licensed under the Mozilla Public License Version 2.0.

See [LICENSE](LICENSE) for the full text.
