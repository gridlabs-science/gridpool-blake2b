#!/usr/bin/env python3
"""Fetch GridPool state bundles and check proof payout outputs against contexts.

This is intentionally dependency-free so it works on small Linux hosts that may
not have Node.js installed. It checks the payout-list part of share validation:
for each bundled proof, the positive coinbase outputs after slot 0 must match
one of the advertised payout variants for that proof's snapshot context.
"""

from __future__ import annotations

import json
import sys
import urllib.request
from typing import Iterable


DEFAULT_BASES = ["https://main.gridpool.net", "https://test.gridpool.net"]
CHARSET = "qpzry9x8gf2tvdw0s3jn54khce6mua7l"
BECH32_CONST = 1
BECH32M_CONST = 0x2BC830A3


def fetch_json(url: str) -> dict:
    request = urllib.request.Request(
        url,
        headers={
            "Accept": "application/json",
            "User-Agent": "GridPoolBundlePayoutCheck/1.0"
        })
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def read_varint(buf: bytes, offset: int) -> tuple[int, int]:
    prefix = buf[offset]
    offset += 1
    if prefix < 0xFD:
        return prefix, offset
    if prefix == 0xFD:
        return int.from_bytes(buf[offset:offset + 2], "little"), offset + 2
    if prefix == 0xFE:
        return int.from_bytes(buf[offset:offset + 4], "little"), offset + 4
    return int.from_bytes(buf[offset:offset + 8], "little"), offset + 8


def parse_outputs(tx_hex: str) -> list[tuple[int, str]]:
    buf = bytes.fromhex(tx_hex)
    offset = 4
    input_count, offset = read_varint(buf, offset)
    for _ in range(input_count):
        offset += 36
        script_len, offset = read_varint(buf, offset)
        offset += script_len + 4

    output_count, offset = read_varint(buf, offset)
    outputs: list[tuple[int, str]] = []
    for _ in range(output_count):
        value = int.from_bytes(buf[offset:offset + 8], "little")
        offset += 8
        script_len, offset = read_varint(buf, offset)
        script = buf[offset:offset + script_len].hex()
        offset += script_len
        outputs.append((value, script.lower()))
    return outputs


def aggregate_outputs(outputs: Iterable[tuple[int, str]]) -> dict[str, int]:
    aggregate: dict[str, int] = {}
    for value, script in outputs:
        if value <= 0:
            continue
        aggregate[script] = aggregate.get(script, 0) + value
    return aggregate


def base58_decode(value: str) -> bytes:
    alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"
    number = 0
    for char in value:
        number = number * 58 + alphabet.index(char)
    payload = number.to_bytes((number.bit_length() + 7) // 8, "big")
    leading_zeroes = len(value) - len(value.lstrip("1"))
    return b"\x00" * leading_zeroes + payload


def bech32_polymod(values: Iterable[int]) -> int:
    generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3]
    chk = 1
    for value in values:
        top = chk >> 25
        chk = ((chk & 0x1FFFFFF) << 5) ^ value
        for i in range(5):
            if (top >> i) & 1:
                chk ^= generator[i]
    return chk


def bech32_hrp_expand(hrp: str) -> list[int]:
    return [ord(char) >> 5 for char in hrp] + [0] + [ord(char) & 31 for char in hrp]


def bech32_decode(address: str) -> tuple[str, list[int], int]:
    if address.lower() != address and address.upper() != address:
        raise ValueError("mixed-case bech32 address")
    address = address.lower()
    pos = address.rfind("1")
    if pos < 1 or pos + 7 > len(address):
        raise ValueError("invalid bech32 separator")
    hrp = address[:pos]
    data = [CHARSET.index(char) for char in address[pos + 1:]]
    check = bech32_polymod(bech32_hrp_expand(hrp) + data)
    if check == BECH32_CONST:
        spec = BECH32_CONST
    elif check == BECH32M_CONST:
        spec = BECH32M_CONST
    else:
        raise ValueError("invalid bech32 checksum")
    return hrp, data[:-6], spec


def convert_bits(data: Iterable[int], from_bits: int, to_bits: int, pad: bool) -> bytes:
    acc = 0
    bits = 0
    ret: list[int] = []
    maxv = (1 << to_bits) - 1
    for value in data:
        if value < 0 or value >> from_bits:
            raise ValueError("invalid bech32 data value")
        acc = (acc << from_bits) | value
        bits += from_bits
        while bits >= to_bits:
            bits -= to_bits
            ret.append((acc >> bits) & maxv)
    if pad:
        if bits:
            ret.append((acc << (to_bits - bits)) & maxv)
    elif bits >= from_bits or ((acc << (to_bits - bits)) & maxv):
        raise ValueError("invalid bech32 padding")
    return bytes(ret)


def address_to_script(address: str) -> str:
    if address.startswith(("bc1", "tb1", "bcrt1")):
        _, data, spec = bech32_decode(address)
        version = data[0]
        program = convert_bits(data[1:], 5, 8, False)
        if version == 0 and spec != BECH32_CONST:
            raise ValueError("v0 witness address must use bech32")
        if version != 0 and spec != BECH32M_CONST:
            raise ValueError("v1+ witness address must use bech32m")
        op = 0 if version == 0 else 0x50 + version
        return bytes([op, len(program)]).hex() + program.hex()

    decoded = base58_decode(address)
    if len(decoded) < 5:
        raise ValueError("invalid base58 address")
    version = decoded[0]
    payload = decoded[1:-4]
    if version in (0x00, 0x6F):
        return "76a914" + payload.hex() + "88ac"
    if version in (0x05, 0xC4):
        return "a914" + payload.hex() + "87"
    raise ValueError(f"unsupported address version {version}")


def expected_map(winners: list[dict]) -> dict[str, int]:
    outputs: dict[str, int] = {}
    for winner in winners:
        value = int(winner.get("value") or 0)
        if value <= 0:
            continue
        script = address_to_script(str(winner.get("address") or "")).lower()
        outputs[script] = outputs.get(script, 0) + value
    return outputs


def proof_variants(bundle: dict, proof: dict, contexts: dict[str, dict]) -> list[dict[str, int]]:
    variants: list[dict[str, int]] = []
    context = contexts.get(proof.get("payoutSnapshotId") or "")
    if context:
        for key in ("winnersList", "feeFreeWinnersList"):
            winners = context.get(key) or []
            if winners:
                variants.append(expected_map(winners))
    fallback = bundle.get("proofWinnersList") or bundle.get("winnersList") or []
    if fallback:
        variants.append(expected_map(fallback))
    return variants


def summarize_bundle(bundle: dict) -> tuple[int, list[str]]:
    contexts = {context.get("snapshotId"): context for context in bundle.get("snapshotContexts", []) if context.get("snapshotId")}
    failures: list[str] = []
    checked = 0
    for section in ("shareProofs", "workSetProofs"):
        for proof in bundle.get(section, []):
            checked += 1
            actual = aggregate_outputs(parse_outputs(proof["coinbaseHex"])[1:])
            variants = proof_variants(bundle, proof, contexts)
            if not variants or not any(actual == variant for variant in variants):
                failures.append(
                    f"{section} share={str(proof.get('shareId'))[:12]} "
                    f"snapshot={str(proof.get('payoutSnapshotId') or '')[:12]} "
                    f"actual={actual}"
                )
    return checked, failures


def main() -> int:
    bases = sys.argv[1:] or DEFAULT_BASES
    failed = False
    for base in bases:
        base = base.rstrip("/")
        print(f"\n{base}")
        summary = fetch_json(f"{base}/api/network/summary")
        print(f"  network={summary.get('networkId')} protocol={summary.get('protocolVersion')} round={summary.get('currentRoundNumber')}")
        for state_id in dict.fromkeys([summary.get("currentStateId"), summary.get("candidateStateId")]):
            if not state_id:
                continue
            bundle = fetch_json(f"{base}/api/network/state/{state_id}")
            checked, failures = summarize_bundle(bundle)
            label = "OK" if not failures else "FAIL"
            print(f"  {label} {bundle.get('kind')} {state_id[:12]} checked={checked} failures={len(failures)}")
            for failure in failures[:10]:
                print(f"    {failure}")
            failed = failed or bool(failures)
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
