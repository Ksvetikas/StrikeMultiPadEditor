# Backup005 reverse-engineering analysis

Source: `Backup005.bkf`  
Size: 153,079,310 bytes  
SHA-256: `3343cf6d2e730fc8b64ed2aba446075a49c890020a0819662128fabb2fa0845b`

## Inventory

The backup contains 97 directory entries:

| Type | Count |
|---|---:|
| KITC | 12 |
| KITH | 58 |
| WLTH | 1 |
| SLTH | 4 |
| INTH | 11 |
| RIFF | 11 |

There are 1,584 KITI signature matches in kit payloads: 12 KITC entries × 16 matches and 58 KITH entries × 24 matches.

## Confirmed observations from this backup

- The BKF directory contains 97 `FILEH` markers.
- The little-endian 32-bit value at `FILEH + 0x10` is used as the next-entry offset for the first 96 entries.
- Entry payload begins at `FILEH + 0x180` in the entries inspected.
- The first 70 entries have 10,240-byte spans; later entry spans vary.
- The final next-entry pointer is 18 bytes beyond physical EOF; physical EOF is used as the final inventory boundary.
- Eleven embedded RIFF/WAVE payloads were extracted using their RIFF-declared lengths.
- The extracted audio inventory identifies all 11 payloads as PCM, stereo, 44.1 kHz, 16-bit.

## Sample-reference examples

| KITI reference | Translation record | Sample-name record | Resolved name |
|---|---|---:|---|
| `1F83` | `1F83 9F80 5040 0000` | `0x003F` | `17 TimpF#1 b` |
| `1F9C` | `1F9C 9F9D 505D 0000` | `0x005C` | `808Low` |

These are verified examples in this backup, not proof that every reference follows the same indexing rule.

## Interpretation cautions

- KITI blocks at offsets `+0x80` and `+0xE0` are recorded as candidate layer blocks; their semantic interpretation remains provisional.
- Signature hits and byte-pattern matches are structural evidence, not a complete firmware specification.
- The relationship between embedded RIFF entries and individual kit slots remains to be established.
- No original backup bytes were modified.

## Files in this repository

- [BKF entry inventory](bkf_entry_inventory.csv)
- [Embedded audio inventory](embedded_audio_inventory.csv)

The full local analysis ZIP also contains the 11 extracted WAV files and a detailed KITI-reference inventory. It is approximately 120 MB and is not included in this repository commit. The 700 KB detailed KITI CSV is also retained in the local analysis ZIP rather than duplicated here.

## Next steps

1. Implement a directory reader based on the `FILEH + 0x10` next-entry pointer.
2. Resolve all KITI references against WLTH/SLTH metadata and report unresolved/duplicate references.
3. Establish which KITI regions correspond to actual pad/layer slots.
4. Map embedded audio entries to sample metadata.
5. Validate candidate parameter IDs through controlled backup comparisons.
