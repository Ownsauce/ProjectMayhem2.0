from ursina import *
import asyncio
import json
import threading
import socket

SERVER_IP = "127.0.0.1"
SERVER_PORT = 4000

# Networking globals
sock = None
incoming_packets = []

def network_thread():
    global incoming_packets
    while True:
        data = sock.recv(4096).decode()
        for line in data.split("\n"):
            if line.strip():
                incoming_packets.append(line.strip())


# -------------------------------
# Ursina Game Logic
# -------------------------------
app = Ursina()

player = Entity(model='cube', color=color.azure, scale=1, position=(0,0,0))
other_players = {}  # addr -> Entity


def update():
    global sock

    speed = 4 * time.dt

    moved = False
    if held_keys['w']:
        player.z -= speed
        moved = True
    if held_keys['s']:
        player.z += speed
        moved = True
    if held_keys['a']:
        player.x -= speed
        moved = True
    if held_keys['d']:
        player.x += speed
        moved = True

    # Send movement to server
    if moved:
        msg = json.dumps({
            "type": "move",
            "x": player.x,
            "y": player.z
        })
        sock.send((msg + "\n").encode())

    # Process incoming packets
    while incoming_packets:
        msg = incoming_packets.pop(0)
        packet = json.loads(msg)

        if packet["type"] == "init":
            for addr, p in packet["players"].items():
                if addr not in other_players:
                    other_players[addr] = Entity(
                        model='cube',
                        color=color.red,
                        scale=1,
                        position=(p["x"], 0, p["y"])
                    )

        elif packet["type"] == "update":
            addr = packet["addr"]

            if addr not in other_players:
                other_players[addr] = Entity(
                    model='cube',
                    color=color.red,
                    scale=1
                )

            other_players[addr].x = packet["x"]
            other_players[addr].z = packet["y"]


# -------------------------------
# Connect to server
# -------------------------------
sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
sock.connect((SERVER_IP, SERVER_PORT))
print("Connected to server!")

threading.Thread(target=network_thread, daemon=True).start()

app.run()
