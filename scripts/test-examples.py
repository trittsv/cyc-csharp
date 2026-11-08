#!/usr/bin/env python3
# Copyright(c) 2026 Cyclone DDS C# contributors
#
# This program and the accompanying materials are made available under the
# terms of the Eclipse Public License v. 2.0 which is available at
# http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
# v. 1.0 which is available at
# http://www.eclipse.org/org/documents/edl-v10.php.
#
# SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
"""Exercise every example as separate processes; optionally pair with the upstream C++ executables."""
import argparse
import itertools
import os
from pathlib import Path
import re
import signal
import subprocess
import time

ROOT = Path(__file__).resolve().parent.parent


class Peer:
    def __init__(self, command, environment, log):
        self.command = [str(x) for x in command]
        self.log = log
        self.stream = log.open("w", encoding="utf-8")
        self.process = subprocess.Popen(self.command, env=environment, stdout=self.stream, stderr=subprocess.STDOUT)

    def output(self):
        self.stream.flush()
        return self.log.read_text(encoding="utf-8", errors="replace")

    def ready(self, marker, timeout=10):
        deadline = time.monotonic() + timeout
        while marker not in self.output():
            if self.process.poll() is not None:
                raise AssertionError(f"Exited before {marker!r}: {self.command}\n{self.output()}")
            if time.monotonic() >= deadline:
                raise TimeoutError(f"No {marker!r}: {self.command}\n{self.output()}")
            time.sleep(0.02)

    def finish(self, timeout=20):
        try:
            code = self.process.wait(timeout=timeout)
        except subprocess.TimeoutExpired as error:
            raise AssertionError(f"Peer did not finish: {self.command}\n{self.output()}") from error
        if code != 0:
            raise AssertionError(f"Peer exited {code}: {self.command}\n{self.output()}")
        return self.output()

    def close(self):
        if self.process.poll() is None:
            self.process.kill()
            self.process.wait(timeout=5)
        self.stream.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--native", type=Path, required=True)
    parser.add_argument("--cxx-bin", type=Path, help="Optional upstream C++ build/bin directory")
    parser.add_argument("--filter", help="Run scenarios whose names contain this text")
    args = parser.parse_args()
    logs = args.build.resolve() / "example-tests" / str(os.getpid())
    logs.mkdir(parents=True, exist_ok=True)
    environment = os.environ.copy()
    environment["CYCLONEDDS_CSHARP_NATIVE"] = str(args.native.resolve())
    # The optional C++ reference apps use domain 0. Managed-only tests use their
    # own domain and remain independent of the binding suite running in domain 41.
    environment["CYCLONEDDS_EXAMPLE_DOMAIN_ID"] = "0" if args.cxx_bin else "73"
    sequence = itertools.count(1)
    peers = []
    passed = 0

    def app(name, *parameters):
        return ["dotnet", ROOT / f"examples/{name}/bin/Release/net9.0/{name}.dll", *map(str, parameters)]

    def cpp(name, *parameters):
        path = args.cxx_bin.resolve() / (name + (".exe" if os.name == "nt" else ""))
        if not path.is_file():
            raise AssertionError(f"Missing upstream executable: {path}")
        return [path, *map(str, parameters)]

    def start(command):
        peer = Peer(command, environment, logs / f"{next(sequence):03d}.log")
        peers.append(peer)
        return peer

    def check(name, test):
        nonlocal passed
        if args.filter and args.filter.casefold() not in name.casefold():
            return
        first = len(peers)
        try:
            test()
            passed += 1
            print(f"PASS {name}", flush=True)
        finally:
            for peer in peers[first:]:
                peer.close()

    def hello(native_publisher=False, native_subscriber=False):
        reader = start(cpp("ddscxxHelloworldSubscriber") if native_subscriber else app("HelloWorld", "sub"))
        if not native_subscriber:
            reader.ready("Waiting for a message")
        writer = start(cpp("ddscxxHelloworldPublisher") if native_publisher else app("HelloWorld", "pub"))
        writer.finish()
        output = reader.finish()
        if "Hello World" not in output:
            raise AssertionError(output)

    def throughput(delay, payload=256, publisher_timeout=0, default_publisher=False, native_publisher=False, native_subscriber=False, writer_count=1):
        partition = f"CSharp examples {os.getpid()} {next(sequence)}"
        subscriber_parameters = [0 if publisher_timeout else 1, delay, partition]
        if default_publisher:
            reader = start(app("Throughput", "sub", 1))
        else:
            reader = start(cpp("cxxThroughputSubscriber", *subscriber_parameters) if native_subscriber else app("Throughput", "sub", *subscriber_parameters))
        if not native_subscriber:
            reader.ready("Waiting for readers and writers")
        publisher_parameters = [payload, 10, 5, publisher_timeout, partition]
        writers = [start(app("Throughput", "pub") if default_publisher else
                         cpp("cxxThroughputPublisher", *publisher_parameters) if native_publisher else
                         app("Throughput", "pub", *publisher_parameters)) for _ in range(writer_count)]
        if publisher_timeout:
            for writer in writers:
                writer.finish()
        output = reader.finish()
        for writer in writers:
            writer.finish()
        totals = re.findall(r"Total received: (\d+) samples, (\d+) bytes", output)
        if not totals:
            raise AssertionError(output)
        samples, byte_count = map(int, totals[-1])
        expected_payload = 8192 if default_publisher else payload
        if samples == 0 or byte_count != samples * expected_payload or "Out of order: 0 samples" not in output:
            raise AssertionError(output)
        if not all(word in output for word in ("samples/s", "Mbit/s")):
            raise AssertionError(output)

    def roundtrip(ping_listener, pong_listener, payload=16, native_ping=False, native_pong=False, timed=False):
        pong_parameters = ["-l"] if pong_listener else []
        pong = start(cpp("cxxRoundtripPong", *pong_parameters) if native_pong else app("RoundTrip", "pong", *pong_parameters))
        pong.ready("Waiting for readers and writers")
        ping_parameters = (["-l"] if ping_listener else []) + [payload, 1 if native_ping else 0 if timed else 8, 1 if timed else 15]
        ping = start(cpp("cxxRoundtripPing", *ping_parameters) if native_ping else app("RoundTrip", "ping", *ping_parameters))
        output = ping.finish(timeout=25)
        if "Overall" not in output:
            raise AssertionError(output)
        if not native_ping and (not timed and "Overall Count: 8" not in output or timed and re.search(r"Overall Count: [1-9]\d*", output) is None):
            raise AssertionError(output)
        start(app("RoundTrip", "ping", "quit")).finish(timeout=10)
        if "Quitting Pong" not in pong.finish(timeout=10):
            raise AssertionError(pong.output())

    def command_line():
        for name, role in (("HelloWorld", "pub"), ("HelloWorld", "sub"), ("Throughput", "pub"), ("Throughput", "sub"), ("RoundTrip", "ping"), ("RoundTrip", "pong")):
            for parameters in (("--help",), (role, "--help")):
                output = start(app(name, *parameters)).finish()
                if "Usage" not in output and "Defaults" not in output:
                    raise AssertionError(f"Missing help for {name}")
        invalid = [("HelloWorld", "invalid"), ("Throughput", "pub", "-1"), ("Throughput", "pub", "104857601"),
                   ("Throughput", "pub", "256", "0", "0"), ("Throughput", "sub", "0", "-2"),
                   ("RoundTrip", "ping", "-1"), ("RoundTrip", "ping", "0", "bad"),
                   ("RoundTrip", "pong", "-2"), ("RoundTrip", "unknown")]
        for command in invalid:
            peer = start(app(*command))
            if peer.process.wait(timeout=10) != 2:
                raise AssertionError(f"Expected argument failure: {command}\n{peer.output()}")

    def cancel(signum):
        peer = start(app("HelloWorld", "pub"))
        peer.ready("Waiting for readers and writers")
        peer.process.send_signal(signum)
        peer.finish(timeout=5)

    def cancel_transfer():
        partition = f"Cancel transfer {os.getpid()}"
        reader = start(app("Throughput", "sub", 0, -1, partition))
        reader.ready("Waiting for readers and writers")
        writer = start(app("Throughput", "pub", 256, 10, 5, 0, partition))
        reader.ready("Transfer rate:")
        writer.process.send_signal(signal.SIGINT)
        writer.finish(timeout=10)
        output = reader.finish(timeout=10)
        if not re.search(r"Total received: [1-9]\d* samples", output):
            raise AssertionError(output)

    try:
        check("all roles: help and invalid arguments", command_line)
        check("HelloWorld: publisher/subscriber", hello)
        check("Throughput: default publisher and listener", lambda: throughput(-1, default_publisher=True))
        check("Throughput: listener, bursts, per-publisher sequence and byte accounting", lambda: throughput(-1))
        check("Throughput: two independent publishers", lambda: throughput(-1, writer_count=2))
        check("Throughput: waitset", lambda: throughput(0))
        check("Throughput: polling with a fragmented payload", lambda: throughput(5, payload=65536))
        check("Throughput: publisher timeout and zero-byte payload", lambda: throughput(-1, payload=0, publisher_timeout=1))
        for ping_listener, pong_listener in itertools.product((False, True), repeat=2):
            check(f"RoundTrip: ping listener={ping_listener}, pong listener={pong_listener}",
                  lambda a=ping_listener, b=pong_listener: roundtrip(a, b, payload=0 if not a and not b else 16))
        check("RoundTrip: fragmented payload and measurement timeout", lambda: roundtrip(False, True, payload=65536, timed=True))
        if os.name != "nt":
            check("SIGINT cancels discovery", lambda: cancel(signal.SIGINT))
            check("SIGTERM cancels discovery", lambda: cancel(signal.SIGTERM))
            check("SIGINT flushes an active Throughput publisher", cancel_transfer)
        if args.cxx_bin:
            check("C++ HelloWorld publisher -> C# subscriber", lambda: hello(native_publisher=True))
            check("C# HelloWorld publisher -> C++ subscriber", lambda: hello(native_subscriber=True))
            check("C++ Throughput publisher -> C# subscriber", lambda: throughput(-1, publisher_timeout=2, native_publisher=True))
            check("C# Throughput publisher -> C++ subscriber", lambda: throughput(0, native_subscriber=True))
            check("C# Ping -> C++ Pong", lambda: roundtrip(False, False, native_pong=True))
            check("C++ Ping -> C# Pong", lambda: roundtrip(False, True, native_ping=True))
    finally:
        for peer in peers:
            if not peer.stream.closed:
                peer.close()
    print(f"{passed} example scenarios passed. Process logs: {logs}", flush=True)


if __name__ == "__main__":
    main()
