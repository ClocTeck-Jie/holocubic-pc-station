#!/usr/bin/env python3
from __future__ import annotations

import argparse
import asyncio
import base64
import contextlib
import hashlib
import io
import signal
import socket
import struct
import sys
import time
from dataclasses import dataclass
from typing import Iterable


MAGIC = b"DMJ1"
VERSION = 1
HEADER = struct.Struct("<4sBBHIIHHHHHHHHHH")
HEADER_LEN = HEADER.size
DEFAULT_FPS = 24.0
DEFAULT_JPEG_QUALITY = 75
DEFAULT_SEND_TIMEOUT_MS = 1000
DEFAULT_MAX_PENDING_KB = 64


def ms_since(start: float) -> int:
    return max(0, min(65535, int((time.perf_counter() - start) * 1000 + 0.5)))


def clamp_u16(value: float | int) -> int:
    return max(0, min(65535, int(value)))


@dataclass
class FrameStats:
    capture_ms: int = 0
    resize_ms: int = 0
    encode_ms: int = 0
    frame_ms: int = 0
    send_prev_ms: int = 0
    clients: int = 0


@dataclass
class ClientState:
    writer: asyncio.StreamWriter


def parse_region(value: str) -> tuple[int, int, int, int]:
    parts = [p.strip() for p in value.replace(";", ",").split(",")]
    if len(parts) != 4:
        raise argparse.ArgumentTypeError("region must be x,y,width,height")
    try:
        x, y, w, h = (int(p) for p in parts)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("region values must be integers") from exc
    if w <= 0 or h <= 0:
        raise argparse.ArgumentTypeError("region width/height must be positive")
    return x, y, w, h


def parse_resolution(value: str) -> tuple[int, int]:
    normalized = value.lower().replace("*", "x").replace(",", "x")
    parts = [p.strip() for p in normalized.split("x")]
    if len(parts) != 2:
        raise argparse.ArgumentTypeError("resolution must be WIDTHxHEIGHT")
    try:
        width, height = (int(p) for p in parts)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("resolution values must be integers") from exc
    if width <= 0 or height <= 0:
        raise argparse.ArgumentTypeError("resolution width/height must be positive")
    return width, height


def crop_region_to_aspect(region: tuple[int, int, int, int], aspect: float) -> tuple[int, int, int, int]:
    left, top, width, height = region
    current = width / height
    if abs(current - aspect) < 0.001:
        return region
    if current > aspect:
        new_width = int(height * aspect)
        left += (width - new_width) // 2
        width = new_width
    else:
        new_height = int(width / aspect)
        top += (height - new_height) // 2
        height = new_height
    return left, top, max(1, width), max(1, height)


def local_ipv4_addresses() -> list[str]:
    addresses: set[str] = set()
    with contextlib.suppress(Exception):
        host = socket.gethostname()
        for item in socket.getaddrinfo(host, None, socket.AF_INET):
            addresses.add(item[4][0])
    with contextlib.suppress(Exception):
        probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            probe.connect(("8.8.8.8", 80))
            addresses.add(probe.getsockname()[0])
        finally:
            probe.close()
    return sorted(ip for ip in addresses if not ip.startswith("127."))


class ScreenSource:
    def __init__(
        self,
        monitor: int,
        monitor_resolution: tuple[int, int] | None,
        list_monitors: bool,
        test_pattern: bool,
        region: tuple[int, int, int, int] | None,
        keep_4_3: bool,
        source_mode: str = "screen",
    ) -> None:
        try:
            from PIL import Image, ImageDraw, ImageOps
        except ImportError as exc:
            raise SystemExit("Missing dependency: Pillow. Install with: py -3 -m pip install -r requirements.txt") from exc

        self.Image = Image
        self.ImageDraw = ImageDraw
        self.ImageOps = ImageOps
        self.test_pattern = test_pattern
        self.source_mode = source_mode
        self.tick = 0
        self.sct = None
        self.monitor = None
        self.region = None

        if not test_pattern:
            try:
                import mss
            except ImportError as exc:
                raise SystemExit("Missing dependency: mss. Install with: py -3 -m pip install -r requirements.txt") from exc

            self.sct = mss.mss()
            monitors = self.sct.monitors
            if list_monitors:
                for idx, item in enumerate(monitors[1:], start=1):
                    print(
                        "[monitor] "
                        f"index={idx} left={item['left']} top={item['top']} "
                        f"size={item['width']}x{item['height']}"
                    )
                raise SystemExit(0)

            if monitor_resolution:
                want_w, want_h = monitor_resolution
                matches = [
                    idx
                    for idx, item in enumerate(monitors[1:], start=1)
                    if int(item["width"]) == want_w and int(item["height"]) == want_h
                ]
                if not matches:
                    available = ", ".join(
                        f"{idx}:{item['width']}x{item['height']}"
                        for idx, item in enumerate(monitors[1:], start=1)
                    )
                    raise SystemExit(f"No monitor matches {want_w}x{want_h}; available monitors: {available or 'none'}")
                monitor = matches[0]
                print(f"[capture] selected monitor={monitor} by resolution={want_w}x{want_h}")

            if monitor < 1 or monitor >= len(monitors):
                raise SystemExit(f"Monitor index {monitor} is invalid; available: 1..{len(monitors) - 1}")
            self.monitor = monitors[monitor]
            base_region = region or (
                int(self.monitor["left"]),
                int(self.monitor["top"]),
                int(self.monitor["width"]),
                int(self.monitor["height"]),
            )
            if keep_4_3:
                base_region = crop_region_to_aspect(base_region, 4 / 3)
            self.region = {
                "left": base_region[0],
                "top": base_region[1],
                "width": base_region[2],
                "height": base_region[3],
            }
            print(
                "[capture] region="
                f"{self.region['left']},{self.region['top']},"
                f"{self.region['width']},{self.region['height']}"
            )
            print(f"[capture] source={self.source_mode}", flush=True)

    def grab(self):
        if self.test_pattern:
            return self._pattern()
        shot = self.sct.grab(self.region or self.monitor)
        return self.Image.frombytes("RGB", shot.size, shot.rgb)

    def _pattern(self):
        self.tick += 1
        img = self.Image.new("RGB", (640, 360), (12, 16, 22))
        draw = self.ImageDraw.Draw(img)
        x = (self.tick * 11) % 640
        y = (self.tick * 7) % 360
        draw.rectangle((0, 0, 639, 359), outline=(70, 80, 96))
        draw.ellipse((x - 45, y - 45, x + 45, y + 45), fill=(55, 155, 255))
        draw.rectangle((40, 56, 600, 126), fill=(28, 36, 48))
        draw.text((62, 78), f"Desktop mirror test frame {self.tick}", fill=(245, 248, 255))
        return img

    def resize(self, img, width: int, height: int, fit: str):
        resample = getattr(getattr(self.Image, "Resampling", self.Image), "BILINEAR")
        if fit == "stretch":
            return img.resize((width, height), resample)
        if fit == "cover":
            return self.ImageOps.fit(img, (width, height), method=resample, centering=(0.5, 0.5))
        inner = self.ImageOps.contain(img, (width, height), method=resample)
        canvas = self.Image.new("RGB", (width, height), (0, 0, 0))
        canvas.paste(inner, ((width - inner.width) // 2, (height - inner.height) // 2))
        return canvas

    @staticmethod
    def encode_jpeg(img, quality: int) -> bytes:
        out = io.BytesIO()
        img.save(out, format="JPEG", quality=quality, optimize=False, progressive=False, subsampling=2)
        return out.getvalue()

    def capture_jpeg(self, width: int, height: int, fit: str, quality: int) -> tuple[bytes, int, int, int]:
        t0 = time.perf_counter()
        grabbed = self.grab()
        capture_ms = ms_since(t0)

        t1 = time.perf_counter()
        frame = self.resize(grabbed, width, height, fit)
        resize_ms = ms_since(t1)

        t2 = time.perf_counter()
        jpeg = self.encode_jpeg(frame, quality)
        encode_ms = ms_since(t2)
        return jpeg, capture_ms, resize_ms, encode_ms


class DxcamScreenSource:
    def __init__(
        self,
        monitor: int,
        monitor_resolution: tuple[int, int] | None,
        list_monitors: bool,
        region: tuple[int, int, int, int] | None,
        keep_4_3: bool,
        backend: str,
        live: bool,
        video_mode: bool,
        target_fps: float,
        source_mode: str = "screen",
    ) -> None:
        try:
            import cv2
            import dxcam
            import mss
            import numpy as np
        except ImportError as exc:
            raise SystemExit(
                "Missing dxcam backend dependency. Install with: py -3 -m pip install dxcam opencv-python numpy mss"
            ) from exc

        self.cv2 = cv2
        self.np = np
        self.last_frame = None
        self.live = live
        self.source_mode = source_mode

        with mss.mss() as sct:
            monitors = sct.monitors
            if list_monitors:
                for idx, item in enumerate(monitors[1:], start=1):
                    print(
                        "[monitor] "
                        f"index={idx} left={item['left']} top={item['top']} "
                        f"size={item['width']}x{item['height']}"
                    )
                raise SystemExit(0)

            if monitor_resolution:
                want_w, want_h = monitor_resolution
                matches = [
                    idx
                    for idx, item in enumerate(monitors[1:], start=1)
                    if int(item["width"]) == want_w and int(item["height"]) == want_h
                ]
                if not matches:
                    available = ", ".join(
                        f"{idx}:{item['width']}x{item['height']}"
                        for idx, item in enumerate(monitors[1:], start=1)
                    )
                    raise SystemExit(f"No monitor matches {want_w}x{want_h}; available monitors: {available or 'none'}")
                monitor = matches[0]
                print(f"[capture] selected monitor={monitor} by resolution={want_w}x{want_h}")

            if monitor < 1 or monitor >= len(monitors):
                raise SystemExit(f"Monitor index {monitor} is invalid; available: 1..{len(monitors) - 1}")

            mon = monitors[monitor]
            self.monitor_left = int(mon["left"])
            self.monitor_top = int(mon["top"])
            self.monitor_width = int(mon["width"])
            self.monitor_height = int(mon["height"])

        base_region = region or (self.monitor_left, self.monitor_top, self.monitor_width, self.monitor_height)
        if keep_4_3:
            base_region = crop_region_to_aspect(base_region, 4 / 3)

        self.capture_width = base_region[2]
        self.capture_height = base_region[3]
        full_monitor = (
            base_region[0] == self.monitor_left
            and base_region[1] == self.monitor_top
            and base_region[2] == self.monitor_width
            and base_region[3] == self.monitor_height
        )
        self.region = None
        if not full_monitor:
            rel_left = base_region[0] - self.monitor_left
            rel_top = base_region[1] - self.monitor_top
            self.region = (rel_left, rel_top, rel_left + base_region[2], rel_top + base_region[3])

        create_kwargs = {
            "output_idx": monitor - 1,
            "output_color": "BGR",
            "backend": backend,
        }
        try:
            self.camera = dxcam.create(**create_kwargs)
        except TypeError:
            if backend != "dxgi":
                raise
            create_kwargs.pop("backend", None)
            self.camera = dxcam.create(**create_kwargs)
        if not self.camera:
            raise SystemExit("dxcam.create() failed")
        if self.live:
            self.camera.start(
                region=self.region,
                target_fps=max(1, int(target_fps + 0.5)),
                video_mode=video_mode,
            )

        print(
            "[capture] backend=dxcam "
            f"dxcam_backend={backend} live={self.live} video_mode={video_mode} "
            f"monitor={monitor} region={base_region[0]},{base_region[1]},{base_region[2]},{base_region[3]}"
        )
        print(f"[capture] source={self.source_mode}", flush=True)

    def _resize(self, frame, width: int, height: int, fit: str):
        cv2 = self.cv2
        src_h, src_w = frame.shape[:2]
        if fit == "stretch":
            return cv2.resize(frame, (width, height), interpolation=cv2.INTER_AREA)

        dst_aspect = width / height
        src_aspect = src_w / src_h
        if fit == "cover":
            if src_aspect > dst_aspect:
                crop_w = int(src_h * dst_aspect)
                x = max(0, (src_w - crop_w) // 2)
                frame = frame[:, x:x + crop_w]
            else:
                crop_h = int(src_w / dst_aspect)
                y = max(0, (src_h - crop_h) // 2)
                frame = frame[y:y + crop_h, :]
            return cv2.resize(frame, (width, height), interpolation=cv2.INTER_AREA)

        scale = min(width / src_w, height / src_h)
        inner_w = max(1, int(src_w * scale))
        inner_h = max(1, int(src_h * scale))
        inner = cv2.resize(frame, (inner_w, inner_h), interpolation=cv2.INTER_AREA)
        canvas = self.np.zeros((height, width, 3), dtype=frame.dtype)
        x = (width - inner_w) // 2
        y = (height - inner_h) // 2
        canvas[y:y + inner_h, x:x + inner_w] = inner
        return canvas

    def capture_jpeg(self, width: int, height: int, fit: str, quality: int) -> tuple[bytes, int, int, int]:
        t0 = time.perf_counter()
        if self.live:
            frame = self.camera.get_latest_frame()
        else:
            frame = self.camera.grab(region=self.region)
        if frame is None:
            frame = self.last_frame
        if frame is None:
            frame = self.np.zeros((self.capture_height, self.capture_width, 3), dtype=self.np.uint8)
        self.last_frame = frame
        capture_ms = ms_since(t0)

        t1 = time.perf_counter()
        resized = self._resize(frame, width, height, fit)
        resize_ms = ms_since(t1)

        t2 = time.perf_counter()
        ok, encoded = self.cv2.imencode(".jpg", resized, [int(self.cv2.IMWRITE_JPEG_QUALITY), int(quality)])
        if not ok:
            raise RuntimeError("cv2.imencode('.jpg') failed")
        encode_ms = ms_since(t2)
        return encoded.tobytes(), capture_ms, resize_ms, encode_ms


class ClientSet:
    def __init__(self) -> None:
        self._clients: dict[asyncio.StreamWriter, ClientState] = {}

    def __len__(self) -> int:
        return len(self._clients)

    def add(self, writer: asyncio.StreamWriter) -> None:
        self._clients[writer] = ClientState(writer=writer)

    def discard(self, writer: asyncio.StreamWriter) -> None:
        self._clients.pop(writer, None)

    async def broadcast_binary(
        self,
        payload: bytes,
        drain_timeout_ms: int = DEFAULT_SEND_TIMEOUT_MS,
        max_pending_bytes: int = DEFAULT_MAX_PENDING_KB * 1024,
    ) -> tuple[int, int, int]:
        if not self._clients:
            return 0, 0, 0

        dead: list[asyncio.StreamWriter] = []
        sent = 0
        skipped = 0
        for writer, state in list(self._clients.items()):
            peer = writer.get_extra_info("peername")
            try:
                transport = getattr(writer, "transport", None)
                if transport is not None:
                    with contextlib.suppress(Exception):
                        transport.set_write_buffer_limits(high=max_pending_bytes, low=max_pending_bytes // 2)
                    if transport.get_write_buffer_size() > max_pending_bytes:
                        skipped += 1
                        continue
                writer.write(pack_ws_frame(payload, opcode=2))
                sent += 1
            except Exception as exc:
                print(f"[ws] client send error {peer}: {exc}", flush=True)
                dead.append(writer)

        for writer in dead:
            self.discard(writer)
            with contextlib.suppress(Exception):
                writer.close()
                await asyncio.wait_for(writer.wait_closed(), timeout=0.5)

        return len(self._clients), sent, skipped


def pack_ws_frame(payload: bytes, opcode: int = 2) -> bytes:
    length = len(payload)
    first = 0x80 | (opcode & 0x0F)
    if length < 126:
        return bytes((first, length)) + payload
    if length <= 0xFFFF:
        return bytes((first, 126)) + struct.pack("!H", length) + payload
    return bytes((first, 127)) + struct.pack("!Q", length) + payload


async def read_ws_frame(reader: asyncio.StreamReader) -> tuple[int, bytes]:
    first_two = await reader.readexactly(2)
    b1, b2 = first_two
    opcode = b1 & 0x0F
    masked = (b2 & 0x80) != 0
    length = b2 & 0x7F
    if length == 126:
        length = struct.unpack("!H", await reader.readexactly(2))[0]
    elif length == 127:
        length = struct.unpack("!Q", await reader.readexactly(8))[0]

    mask = await reader.readexactly(4) if masked else b""
    payload = await reader.readexactly(length) if length else b""
    if masked and payload:
        payload = bytes(byte ^ mask[i & 3] for i, byte in enumerate(payload))
    return opcode, payload


async def handle_client(reader: asyncio.StreamReader, writer: asyncio.StreamWriter, clients: ClientSet) -> None:
    peer = writer.get_extra_info("peername")
    sock = writer.get_extra_info("socket")
    if sock is not None:
        with contextlib.suppress(Exception):
            sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    try:
        request = await reader.readuntil(b"\r\n\r\n")
        headers = {}
        for line in request.decode("latin1", "replace").split("\r\n")[1:]:
            if ":" in line:
                k, v = line.split(":", 1)
                headers[k.strip().lower()] = v.strip()

        key = headers.get("sec-websocket-key")
        if not key:
            writer.write(b"HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n")
            await writer.drain()
            return

        accept = base64.b64encode(
            hashlib.sha1((key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").encode("ascii")).digest()
        ).decode("ascii")
        writer.write(
            (
                "HTTP/1.1 101 Switching Protocols\r\n"
                "Upgrade: websocket\r\n"
                "Connection: Upgrade\r\n"
                f"Sec-WebSocket-Accept: {accept}\r\n"
                "\r\n"
            ).encode("ascii")
        )
        await writer.drain()

        clients.add(writer)
        print(f"[ws] client connected: {peer}, clients={len(clients)}", flush=True)

        while True:
            opcode, payload = await read_ws_frame(reader)
            if opcode == 8:
                with contextlib.suppress(Exception):
                    writer.write(pack_ws_frame(payload[:125], opcode=8))
                    await writer.drain()
                break
            if opcode == 9:
                writer.write(pack_ws_frame(payload[:125], opcode=10))
                await writer.drain()
            # Device-side dropping does not require ACK; consume data/pong frames
            # so close and heartbeat handling can keep this read loop alive.
    except (asyncio.IncompleteReadError, ConnectionError):
        pass
    except Exception as exc:
        print(f"[ws] client error {peer}: {exc}", flush=True)
    finally:
        clients.discard(writer)
        with contextlib.suppress(Exception):
            writer.close()
            await writer.wait_closed()
        print(f"[ws] client closed: {peer}, clients={len(clients)}", flush=True)


def build_payload(
    frame_id: int,
    jpeg: bytes,
    width: int,
    height: int,
    quality: int,
    fps: float,
    stats: FrameStats,
) -> bytes:
    header = HEADER.pack(
        MAGIC,
        VERSION,
        0,
        HEADER_LEN,
        frame_id & 0xFFFFFFFF,
        len(jpeg),
        width,
        height,
        quality,
        clamp_u16(fps * 100),
        stats.capture_ms,
        stats.resize_ms,
        stats.encode_ms,
        stats.frame_ms,
        stats.send_prev_ms,
        stats.clients,
    )
    return header + jpeg


async def stream_loop(args: argparse.Namespace, clients: ClientSet, source) -> None:
    frame_id = 0
    last_send_ms = 0
    last_log = time.perf_counter()
    sent_frames = 0
    interval = 1.0 / max(1.0, args.fps)
    next_frame_at = time.perf_counter()
    print(f"[tx] target_fps={args.fps:.1f} interval={interval * 1000:.1f}ms quality={args.quality}", flush=True)

    while True:
        now = time.perf_counter()
        if now < next_frame_at:
            await asyncio.sleep(next_frame_at - now)

        loop_start = time.perf_counter()
        if len(clients) == 0:
            next_frame_at = time.perf_counter() + interval
            await asyncio.sleep(0.1)
            continue
        jpeg, capture_ms, resize_ms, encode_ms = source.capture_jpeg(
            args.width,
            args.height,
            args.fit,
            args.quality,
        )
        stats = FrameStats(
            capture_ms=capture_ms,
            resize_ms=resize_ms,
            encode_ms=encode_ms,
            frame_ms=ms_since(loop_start),
            send_prev_ms=last_send_ms,
            clients=len(clients),
        )
        payload = build_payload(frame_id, jpeg, args.width, args.height, args.quality, args.fps, stats)

        t3 = time.perf_counter()
        active_clients, sent_clients, skipped_clients = await clients.broadcast_binary(
            payload,
            args.send_timeout_ms,
            args.max_pending_kb * 1024,
        )
        last_send_ms = ms_since(t3)

        frame_id += 1
        sent_frames += 1

        now = time.perf_counter()
        if now - last_log >= 1.0:
            print(
                f"[tx] fps={sent_frames / (now - last_log):.1f}/{args.fps:.1f} clients={active_clients} "
                f"jpg={len(jpeg) / 1024:.1f}KB cap={capture_ms}ms resize={resize_ms}ms "
                f"jpeg={encode_ms}ms send={last_send_ms}ms sent={sent_clients} "
                f"drop_out={skipped_clients}",
                flush=True,
            )
            sent_frames = 0
            last_log = now

        next_frame_at += interval
        if next_frame_at < time.perf_counter():
            next_frame_at = time.perf_counter()


def parse_args(argv: Iterable[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Send 320x240 JPEG desktop frames to the Lua device over WebSocket.")
    parser.add_argument("--host", default="0.0.0.0", help="Bind host, default: 0.0.0.0")
    parser.add_argument("--port", type=int, default=8787, help="Bind port, default: 8787")
    parser.add_argument("--fps", type=float, default=DEFAULT_FPS, help="Target frame rate, default: 24")
    parser.add_argument("--width", type=int, default=320, help="Output width, default: 320")
    parser.add_argument("--height", type=int, default=240, help="Output height, default: 240")
    parser.add_argument("--quality", type=int, default=DEFAULT_JPEG_QUALITY, help="JPEG quality 1..95, default: 75")
    parser.add_argument(
        "--capture",
        choices=("mss", "dxcam"),
        default="mss",
        help="Capture backend. dxcam is faster on many Windows systems but needs optional dependencies.",
    )
    parser.add_argument(
        "--dxcam-backend",
        choices=("dxgi", "winrt"),
        default="dxgi",
        help="dxcam capture backend, default: dxgi. Try winrt if dxgi tears or misses the cursor.",
    )
    parser.add_argument(
        "--dxcam-live",
        action="store_true",
        help="Use dxcam's continuous capture thread and read the latest frame.",
    )
    parser.add_argument(
        "--dxcam-video-mode",
        action="store_true",
        help="With --dxcam-live, reuse the previous frame to keep output pacing stable.",
    )
    parser.add_argument("--monitor", type=int, default=1, help="Monitor index, default: 1")
    parser.add_argument(
        "--source",
        choices=("screen", "virtual-monitor", "region"),
        default="screen",
        help="Capture source: screen uses the selected monitor, virtual-monitor selects a display by resolution, region captures an explicit screen rectangle.",
    )
    parser.add_argument(
        "--monitor-resolution",
        type=parse_resolution,
        help="Auto-select first monitor with this resolution, for example 1600x1200.",
    )
    parser.add_argument("--list-monitors", action="store_true", help="Print available monitors and exit")
    parser.add_argument("--fit", choices=("contain", "cover", "stretch"), default="stretch")
    parser.add_argument(
        "--region",
        type=parse_region,
        help="Capture region as x,y,width,height in screen coordinates.",
    )
    parser.add_argument("--no-keep-4-3", action="store_true", help="Do not crop the capture region to 4:3 first")
    parser.add_argument("--test-pattern", action="store_true", help="Use a generated test pattern instead of screen capture")
    parser.add_argument(
        "--send-timeout-ms",
        type=int,
        default=DEFAULT_SEND_TIMEOUT_MS,
        help="Drop a client if socket drain blocks longer than this, default: 1000",
    )
    parser.add_argument(
        "--max-pending-kb",
        type=int,
        default=DEFAULT_MAX_PENDING_KB,
        help=f"Skip a live frame for a client when its socket output queue exceeds this, default: {DEFAULT_MAX_PENDING_KB}",
    )
    args = parser.parse_args(list(argv))
    if args.source == "virtual-monitor" and not args.monitor_resolution:
        parser.error("--source virtual-monitor requires --monitor-resolution WIDTHxHEIGHT")
    if args.source == "region" and not args.region:
        parser.error("--source region requires --region x,y,width,height")
    if args.source == "region":
        args.no_keep_4_3 = True
    args.quality = max(1, min(95, args.quality))
    args.send_timeout_ms = max(1, args.send_timeout_ms)
    args.max_pending_kb = max(16, args.max_pending_kb)
    return args


async def main_async(args: argparse.Namespace) -> None:
    if args.capture == "dxcam" and not args.test_pattern:
        source = DxcamScreenSource(
            args.monitor,
            args.monitor_resolution,
            args.list_monitors,
            args.region,
            not args.no_keep_4_3,
            args.dxcam_backend,
            args.dxcam_live,
            args.dxcam_video_mode,
            args.fps,
            args.source,
        )
    else:
        source = ScreenSource(
            args.monitor,
            args.monitor_resolution,
            args.list_monitors,
            args.test_pattern,
            args.region,
            not args.no_keep_4_3,
            args.source,
        )

    clients = ClientSet()
    server = await asyncio.start_server(lambda r, w: handle_client(r, w, clients), args.host, args.port)
    addrs = ", ".join(str(sock.getsockname()) for sock in server.sockets or [])
    print(f"[ws] listening on {addrs}", flush=True)
    ips = local_ipv4_addresses()
    if ips:
        print("[hint] set device url to one of:", ", ".join(f"ws://{ip}:{args.port}" for ip in ips), flush=True)

    stream_task = asyncio.create_task(stream_loop(args, clients, source))
    stop_event = asyncio.Event()

    if sys.platform != "win32":
        loop = asyncio.get_running_loop()
        for sig in (signal.SIGINT, signal.SIGTERM):
            loop.add_signal_handler(sig, stop_event.set)

    try:
        async with server:
            if sys.platform == "win32":
                await stream_task
            else:
                await stop_event.wait()
    except KeyboardInterrupt:
        pass
    finally:
        stream_task.cancel()
        with contextlib.suppress(asyncio.CancelledError):
            await stream_task


def main(argv: Iterable[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    try:
        asyncio.run(main_async(args))
    except KeyboardInterrupt:
        return 130
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
