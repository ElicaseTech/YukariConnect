import socket
import struct

sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
sock.connect(("127.0.0.1", 13448))

# c:player_ping 测试
kind = b"c:player_ping"
body = b'{"name":"TestPlayer","machine_id":"0123456789abcdef0123456789abcdef","vendor":"Test"}'

sock.sendall(bytes([len(kind)]) + kind + struct.pack(">I", len(body)) + body)

status = sock.recv(1)[0]
print(f"Status: {status}")
sock.close()
