import asyncio
import json

clients = {}  # addr -> writer
players = {}  # addr -> {x, y, name}

async def handle_client(reader, writer):
    addr = writer.get_extra_info('peername')
    print(f"[+] Connected: {addr}")

    clients[addr] = writer
    players[addr] = {"x": 0, "y": 0, "name": f"Player_{len(players)+1}"}

    # Send initial world state to joining user
    init_msg = json.dumps({"type": "init", "players": players})
    writer.write((init_msg + "\n").encode())
    await writer.drain()

    try:
        while True:
            data = await reader.readline()
            if not data:
                break

            msg = data.decode().strip()
            packet = json.loads(msg)

            if packet["type"] == "move":
                players[addr]["x"] = packet["x"]
                players[addr]["y"] = packet["y"]

                # Broadcast update to all players
                update = json.dumps({
                    "type": "update",
                    "addr": str(addr),
                    "x": packet["x"],
                    "y": packet["y"]
                })

                await broadcast(update)

    except Exception as e:
        print("Error:", e)

    print(f"[-] Disconnected: {addr}")
    del clients[addr]
    del players[addr]
    writer.close()


async def broadcast(msg):
    for w in clients.values():
        w.write((msg + "\n").encode())
        await w.drain()


async def main():
    server = await asyncio.start_server(handle_client, "0.0.0.0", 4000)
    print("[SERVER] Running on port 4000")
    async with server:
        await server.serve_forever()

asyncio.run(main())
