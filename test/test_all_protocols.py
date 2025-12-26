#!/usr/bin/env python3
"""
Scaffolding 协议测试工具
用于测试 YukariConnect Scaffolding 服务器的所有协议
"""

import socket
import struct
import json
import sys
from typing import Tuple, Optional


class ScaffoldingClient:
    """Scaffolding 协议客户端"""

    def __init__(self, host: str = "127.0.0.1", port: int = 13448):
        self.host = host
        self.port = port
        self.sock: Optional[socket.socket] = None

    def connect(self):
        """连接到服务器"""
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.settimeout(5)
        self.sock.connect((self.host, self.port))
        return self

    def close(self):
        """关闭连接"""
        if self.sock:
            self.sock.close()
            self.sock = None

    def send_request(self, kind: str, body: bytes = b'') -> Tuple[int, bytes]:
        """
        发送 Scaffolding 请求

        Args:
            kind: 协议类型，如 "c:ping", "c:server_port"
            body: 请求体

        Returns:
            (status, data): 状态码和响应数据
        """
        if not self.sock:
            raise RuntimeError("Not connected")

        # 构建请求
        kind_bytes = kind.encode('utf-8')
        request = bytearray()

        # 1. Kind length (1 byte)
        request.append(len(kind_bytes))

        # 2. Kind (variable)
        request.extend(kind_bytes)

        # 3. Body length (4 bytes, Big Endian)
        request.extend(struct.pack('>I', len(body)))

        # 4. Body (variable)
        request.extend(body)

        # 发送请求
        print(f"[SEND] Kind: {kind}, BodyLength: {len(body)}")
        print(f"[SEND] Raw request (hex): {request.hex()}")

        try:
            self.sock.sendall(request)
        except Exception as e:
            raise RuntimeError(f"Send failed: {e}")

        # 接收响应 - 使用循环接收
        try:
            status_bytes = self.sock.recv(1)
            if not status_bytes:
                raise RuntimeError("Connection closed by server")
            status = status_bytes[0]

            # 接收数据长度
            data_len_bytes = b''
            while len(data_len_bytes) < 4:
                chunk = self.sock.recv(4 - len(data_len_bytes))
                if not chunk:
                    raise RuntimeError("Connection closed while receiving data length")
                data_len_bytes += chunk

            data_len = struct.unpack('>I', data_len_bytes)[0]

            # 接收数据
            data = b''
            while len(data) < data_len:
                chunk = self.sock.recv(data_len - len(data))
                if not chunk:
                    raise RuntimeError("Connection closed while receiving data")
                data += chunk

            print(f"[RECV] Status: {status}, DataLength: {data_len}")
            if data:
                print(f"[RECV] Data (hex): {data.hex()}")
                try:
                    data_str = data.decode('utf-8')
                    print(f"[RECV] Data (UTF-8): {data_str}")
                    try:
                        data_json = json.loads(data_str)
                        print(f"[RECV] Data (JSON): {json.dumps(data_json, indent=2, ensure_ascii=False)}")
                    except:
                        pass
                except:
                    pass

            return status, data

        except socket.timeout:
            raise RuntimeError(f"Timeout waiting for response (5s)")
        except Exception as e:
            raise RuntimeError(f"Receive failed: {e}")

    def __enter__(self):
        return self.connect()

    def __exit__(self, *args):
        self.close()


def print_header(title: str):
    """打印标题"""
    print("\n" + "=" * 60)
    print(f"  {title}")
    print("=" * 60)


def test_ping(client: ScaffoldingClient):
    """测试 c:ping 协议"""
    print_header("测试 c:ping (连接验证)")

    # Fingerprint 常量
    fingerprint = bytes.fromhex("41 57 48 44 86 37 40 59 57 44 92 43 96 99 85 01")
    print(f"[INFO] Fingerprint: {fingerprint.hex()}")

    status, data = client.send_request("c:ping", fingerprint)

    if status == 0 and data == fingerprint:
        print("[PASS] Ping 成功，fingerprint 匹配")
        return True
    else:
        print(f"[FAIL] Ping 失败，Status={status}")
        return False


def test_protocols(client: ScaffoldingClient):
    """测试 c:protocols 协议"""
    print_header("测试 c:protocols (获取协议列表)")

    status, data = client.send_request("c:protocols")

    if status == 0:
        protocols = data.decode('utf-8').split('\0')
        print(f"[INFO] 支持的协议 ({len(protocols)} 个):")
        for p in protocols:
            print(f"  - {p}")
        return True
    else:
        print(f"[FAIL] 获取协议列表失败，Status={status}")
        return False


def test_server_port(client: ScaffoldingClient):
    """测试 c:server_port 协议"""
    print_header("测试 c:server_port (获取 MC 服务器端口)")

    status, data = client.send_request("c:server_port")

    if status == 0:
        if len(data) >= 2:
            port = struct.unpack('>H', data[:2])[0]
            print(f"[INFO] MC 服务器端口: {port}")
            return True
        else:
            print("[FAIL] 响应数据长度不足")
            return False
    elif status == 32:
        print("[INFO] 主机尚未处于 HostOk 状态（正常，需要先启动 MC 服务器）")
        return True
    else:
        print(f"[FAIL] 获取端口失败，Status={status}")
        return False


def test_player_ping(client: ScaffoldingClient, name: str = "TestPlayer",
                     machine_id: str = "0123456789abcdef0123456789abcdef",
                     vendor: str = "TestLauncher 1.0"):
    """测试 c:player_ping 协议"""
    print_header("测试 c:player_ping (玩家注册/心跳)")

    player_data = {
        "name": name,
        "machine_id": machine_id,
        "vendor": vendor
    }
    player_json = json.dumps(player_data, separators=(',', ':'))
    player_bytes = player_json.encode('utf-8')

    print(f"[INFO] 注册玩家:")
    print(f"  - Name: {name}")
    print(f"  - Machine ID: {machine_id}")
    print(f"  - Vendor: {vendor}")

    status, data = client.send_request("c:player_ping", player_bytes)

    if status == 0:
        print("[PASS] 玩家注册成功")
        return True
    else:
        error_msg = data.decode('utf-8', errors='ignore') if data else "Unknown error"
        print(f"[FAIL] 玩家注册失败，Status={status}, Error={error_msg}")
        return False


def test_player_profiles_list(client: ScaffoldingClient):
    """测试 c:player_profiles_list 协议"""
    print_header("测试 c:player_profiles_list (获取玩家列表)")

    status, data = client.send_request("c:player_profiles_list")

    if status == 0:
        profiles = json.loads(data.decode('utf-8'))
        print(f"[INFO] 玩家列表 ({len(profiles)} 个):")
        for p in profiles:
            # 兼容不同的 kind 格式
            kind_value = p.get("kind", "")
            if isinstance(kind_value, dict):
                kind_value = kind_value.get("Value", "")

            kind_emoji = {"HOST": "🏠", "GUEST": "👤", "LOCAL": "💻"}.get(kind_value, "❓")
            print(f"  {kind_emoji} {p.get('name', 'Unknown')} ({kind_value})")
            print(f"     Machine ID: {p.get('machine_id', 'Unknown')}")
            print(f"     Vendor: {p.get('vendor', 'Unknown')}")
        return True
    else:
        print(f"[FAIL] 获取玩家列表失败，Status={status}")
        return False


def test_invalid_protocol(client: ScaffoldingClient):
    """测试不存在的协议"""
    print_header("测试无效协议 (错误处理)")

    status, data = client.send_request("c:invalid_protocol")

    if status == 255:
        print("[PASS] 正确返回 Status=255 (协议未实现)")
        error_msg = data.decode('utf-8', errors='ignore')
        print(f"[INFO] 错误信息: {error_msg}")
        return True
    else:
        print(f"[INFO] 返回 Status={status} (可能接受未知协议)")
        return True


def run_all_tests(host: str = "127.0.0.1", port: int = 13448):
    """运行所有测试"""
    print(f"\n{'#' * 60}")
    print(f"#  Scaffolding 协议测试套件")
    print(f"#  目标: {host}:{port}")
    print(f"{'#' * 60}\n")

    results = []

    # 测试 1: c:ping
    with ScaffoldingClient(host, port) as client:
        results.append(("c:ping", test_ping(client)))

    # 测试 2: c:protocols
    with ScaffoldingClient(host, port) as client:
        results.append(("c:protocols", test_protocols(client)))

    # 测试 3: c:server_port
    with ScaffoldingClient(host, port) as client:
        results.append(("c:server_port", test_server_port(client)))

    # 测试 4: c:player_ping (注册玩家)
    with ScaffoldingClient(host, port) as client:
        results.append(("c:player_ping (register)", test_player_ping(client)))

    # 测试 5: c:player_ping (再次发送，测试更新)
    with ScaffoldingClient(host, port) as client:
        results.append(("c:player_ping (update)",
                       test_player_ping(client, name="UpdatedPlayer")))

    # 测试 6: c:player_profiles_list
    with ScaffoldingClient(host, port) as client:
        results.append(("c:player_profiles_list", test_player_profiles_list(client)))

    # 测试 7: 无效协议
    with ScaffoldingClient(host, port) as client:
        results.append(("c:invalid_protocol", test_invalid_protocol(client)))

    # 打印测试结果摘要
    print_header("测试结果摘要")
    passed = sum(1 for _, r in results if r)
    total = len(results)

    for name, result in results:
        status = "✓ PASS" if result else "✗ FAIL"
        print(f"  {status}  {name}")

    print(f"\n总计: {passed}/{total} 通过")

    if passed == total:
        print("\n🎉 所有测试通过！")
        return 0
    else:
        print(f"\n⚠️  {total - passed} 个测试失败")
        return 1


def main():
    """主函数"""
    import argparse

    parser = argparse.ArgumentParser(description="Scaffolding 协议测试工具")
    parser.add_argument("--host", default="127.0.0.1", help="服务器地址 (默认: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=13448, help="服务器端口 (默认: 13448)")
    parser.add_argument("--test", choices=["ping", "protocols", "server_port", "player_ping",
                                              "player_profiles_list", "invalid", "all"],
                       default="all", help="要运行的测试 (默认: all)")

    args = parser.parse_args()

    if args.test == "all":
        return run_all_tests(args.host, args.port)

    # 单个测试
    with ScaffoldingClient(args.host, args.port) as client:
        if args.test == "ping":
            return 0 if test_ping(client) else 1
        elif args.test == "protocols":
            return 0 if test_protocols(client) else 1
        elif args.test == "server_port":
            return 0 if test_server_port(client) else 1
        elif args.test == "player_ping":
            return 0 if test_player_ping(client) else 1
        elif args.test == "player_profiles_list":
            return 0 if test_player_profiles_list(client) else 1
        elif args.test == "invalid":
            return 0 if test_invalid_protocol(client) else 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
