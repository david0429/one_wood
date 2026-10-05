#!/usr/bin/env python3
"""GSPro Open Connect v1 test tool for One Wood (Python 3.8+, standard library only).

Two roles:

  send     Act as FS Golf: connect to the game and send messages from shot logs,
           printing every response. Use it to exercise the game without a Mevo+.

  capture  Act as the game: listen on port 921, acknowledge shots like GSPro does,
           and record every message FS Golf sends to a JSON Lines shot log. Run this
           on the simulator PC with the real Mevo+ to find out exactly what FS Golf
           sends, then validate the log in Unity (One Wood > Validate Shot Log...).

Examples:
  python Tools/open_connect.py send Tools/ShotLogs/sample_shots.jsonl
  python Tools/open_connect.py send Tools/ShotLogs/sample_shots.jsonl --framing split --delay 0.5
  python Tools/open_connect.py capture --out OneWood/Logs/shots
  python Tools/open_connect.py capture --club PT          # ask FS Golf for putting mode
"""

import argparse
import datetime as dt
import json
import os
import socket
import sys
import time

DEFAULT_PORT = 921
CLUB_CODES = {
    "DR", "W2", "W3", "W4", "W5", "W6", "W7", "H2", "H3", "H4", "H5", "H6", "H7",
    "I1", "I2", "I3", "I4", "I5", "I6", "I7", "I8", "I9", "PW", "GW", "SW", "LW", "PT",
}


class JsonFramer:
    """Splits a text stream into top-level JSON objects (mirrors JsonMessageFramer.cs)."""

    def __init__(self):
        self.current = []
        self.depth = 0
        self.in_string = False
        self.escaped = False

    def push(self, text):
        messages, junk = [], []
        for c in text:
            if self.depth == 0:
                if c == "{":
                    self.depth = 1
                    self.current.append(c)
                elif not c.isspace():
                    junk.append(c)
                continue
            self.current.append(c)
            if self.in_string:
                if self.escaped:
                    self.escaped = False
                elif c == "\\":
                    self.escaped = True
                elif c == '"':
                    self.in_string = False
            elif c == '"':
                self.in_string = True
            elif c in "{[":
                self.depth += 1
            elif c in "}]":
                self.depth -= 1
                if self.depth == 0:
                    messages.append("".join(self.current))
                    self.current = []
        return messages, "".join(junk)


def load_messages(paths):
    """Reads .json (one object) or .jsonl shot logs; unwraps {"raw": ...} envelopes."""
    messages = []
    for path in paths:
        with open(path, encoding="utf-8") as f:
            if path.endswith(".json"):
                messages.append(json.load(f))
                continue
            for number, line in enumerate(f, 1):
                line = line.strip()
                if not line or line.startswith("#") or line.startswith("//"):
                    continue
                try:
                    obj = json.loads(line)
                except json.JSONDecodeError:
                    print(f"{path}:{number}: sending invalid JSON verbatim", file=sys.stderr)
                    messages.append(line)
                    continue
                if isinstance(obj, dict) and ("raw" in obj or "rawText" in obj):
                    obj = obj.get("raw", obj.get("rawText"))
                messages.append(obj)
    return messages


def describe(obj):
    if not isinstance(obj, dict):
        return "invalid JSON"
    options = obj.get("ShotDataOptions") or {}
    ball = obj.get("BallData") or {}
    prefix = f"#{obj.get('ShotNumber')} " if obj.get("ShotNumber") is not None else ""
    if options.get("IsHeartBeat"):
        return f"{prefix}heartbeat ready={options.get('LaunchMonitorIsReady')} ballDetected={options.get('LaunchMonitorBallDetected')}"
    if not options.get("ContainsBallData", bool(ball)):
        return f"{prefix}status ready={options.get('LaunchMonitorIsReady')} ballDetected={options.get('LaunchMonitorBallDetected')}"
    return (f"{prefix}shot speed={ball.get('Speed')} VLA={ball.get('VLA')} HLA={ball.get('HLA')} "
            f"spin={ball.get('TotalSpin')} axis={ball.get('SpinAxis')} carry={ball.get('CarryDistance')}")


def read_responses(sock, framer, timeout):
    sock.settimeout(timeout)
    responses = []
    try:
        while True:
            data = sock.recv(4096)
            if not data:
                print("  server closed the connection")
                break
            found, junk = framer.push(data.decode("utf-8", errors="replace"))
            if junk:
                print(f"  unexpected non-JSON from server: {junk!r}")
            responses.extend(found)
            if found:
                sock.settimeout(0.05)  # drain anything else that is already queued
    except socket.timeout:
        pass
    return responses


def cmd_send(args):
    messages = load_messages(args.files)
    if not messages:
        sys.exit("No messages found.")
    payloads = [m if isinstance(m, str) else json.dumps(m, separators=(",", ":")) for m in messages]

    with socket.create_connection((args.host, args.port), timeout=5) as sock:
        print(f"Connected to {args.host}:{args.port}; sending {len(payloads)} message(s) ({args.framing} framing)")
        framer = JsonFramer()

        if args.framing == "concat":
            sock.sendall("".join(payloads).encode("utf-8"))
            for message in messages:
                print(f"-> {describe(message)}")
            for response in read_responses(sock, framer, args.response_timeout):
                print(f"<- {response}")
            return

        for message, payload in zip(messages, payloads):
            print(f"-> {describe(message)}")
            data = (payload + ("\n" if args.framing == "newline" else "")).encode("utf-8")
            if args.framing == "split":
                middle = len(data) // 2
                sock.sendall(data[:middle])
                time.sleep(0.05)
                sock.sendall(data[middle:])
            else:
                sock.sendall(data)
            for response in read_responses(sock, framer, args.response_timeout):
                print(f"<- {response}")
            time.sleep(args.delay)


def cmd_capture(args):
    os.makedirs(args.out, exist_ok=True)
    path = os.path.join(args.out, dt.datetime.now().strftime("shots_%Y%m%d_%H%M%S.jsonl"))
    if args.club and args.club not in CLUB_CODES:
        sys.exit(f"Unknown club code {args.club!r}; use one of {' '.join(sorted(CLUB_CODES))}")

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server, open(path, "a", encoding="utf-8") as log:
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server.bind((args.host, args.port))
        server.listen(1)
        print(f"Listening on {args.host}:{args.port}; logging to {path}")
        print("Point FS Golf's GSPro connection at this machine. Ctrl+C to stop.")

        count = 0
        try:
            while True:
                conn, addr = server.accept()
                remote = f"{addr[0]}:{addr[1]}"
                print(f"Client connected from {remote}")
                with conn:
                    if args.club:
                        player = {"Code": 201, "Message": "One Wood Player Information",
                                  "Player": {"Handed": args.handed, "Club": args.club}}
                        conn.sendall(json.dumps(player).encode("utf-8"))
                        print(f"<- player info club={args.club} handed={args.handed}")
                    framer = JsonFramer()
                    while True:
                        data = conn.recv(4096)
                        if not data:
                            print(f"Client {remote} disconnected")
                            break
                        found, junk = framer.push(data.decode("utf-8", errors="replace"))
                        if junk:
                            print(f"  discarded non-JSON: {junk!r}")
                        for raw in found:
                            count += 1
                            entry = {"t": dt.datetime.now(dt.timezone.utc).isoformat(), "remote": remote}
                            try:
                                obj = json.loads(raw)
                                entry["raw"] = obj
                            except json.JSONDecodeError:
                                obj = None
                                entry["rawText"] = raw
                            log.write(json.dumps(entry, separators=(",", ":")) + "\n")
                            log.flush()
                            print(f"-> [{count}] {describe(obj) if obj is not None else 'INVALID JSON: ' + raw[:80]}")

                            if obj is None:
                                reply = {"Code": 501, "Message": "Invalid JSON", "Player": None}
                            else:
                                options = obj.get("ShotDataOptions") or {}
                                is_shot = not options.get("IsHeartBeat") and options.get("ContainsBallData", obj.get("BallData") is not None)
                                reply = {"Code": 200, "Message": "Shot received successfully", "Player": None} \
                                    if is_shot or args.respond_status else None
                            if reply is not None:
                                conn.sendall(json.dumps(reply).encode("utf-8"))
        except KeyboardInterrupt:
            print(f"\nStopped. {count} message(s) written to {path}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    send = sub.add_parser("send", help="act as FS Golf and send shot logs to the game")
    send.add_argument("files", nargs="+", help=".jsonl shot logs or .json single messages")
    send.add_argument("--host", default="127.0.0.1")
    send.add_argument("--port", type=int, default=DEFAULT_PORT)
    send.add_argument("--delay", type=float, default=0.25, help="seconds between messages")
    send.add_argument("--framing", choices=["newline", "concat", "split"], default="newline",
                      help="newline-delimited, all back-to-back in one write, or each split across two writes")
    send.add_argument("--response-timeout", type=float, default=1.0)
    send.set_defaults(func=cmd_send)

    capture = sub.add_parser("capture", help="act as the game and record what FS Golf sends")
    capture.add_argument("--host", default="0.0.0.0")
    capture.add_argument("--port", type=int, default=DEFAULT_PORT)
    capture.add_argument("--out", default=os.path.join("OneWood", "Logs", "shots"), help="directory for the .jsonl log")
    capture.add_argument("--club", help="send player info with this club code on connect (e.g. PT for putting)")
    capture.add_argument("--handed", choices=["RH", "LH"], default="RH")
    capture.add_argument("--respond-status", action="store_true", help="also reply 200 to heartbeats/status messages")
    capture.set_defaults(func=cmd_capture)

    args = parser.parse_args()
    sys.stdout.reconfigure(line_buffering=True)  # show progress promptly when piped or redirected
    args.func(args)


if __name__ == "__main__":
    main()
