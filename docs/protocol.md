# Protocol

This document is the byte-level contract for the Lian Li Uni controllers the plugin drives. All controllers share vendor id `0x0CF2`. Every report begins with byte `224` (`0xE0`), which is the HID report id. The pure encoder strategies in the Protocol layer implement exactly what is described here; the unit tests assert these bytes directly.

## Device matrix

| Family | PID(s) | Set-speed report | Duty->byte formula | Manual-mode report | RPM offset |
| --- | --- | --- | --- | --- | --- |
| Uni Hub 0x7750 (SL) | 0x7750 | {224, 32+ch, 0, B} | B = (byte)((800 + 11\*d) / 19) | {224, 16, 49, (byte)(0x10<<ch)} | 1 |
| Uni SL 0xA100 (SL) | 0xA100 | {224, 32+ch, 0, B} | B = (byte)((800 + 11\*d) / 19) | {224, 16, 49, (byte)(0x10<<ch)} | 1 |
| Uni AL 0xA101 (AL) | 0xA101 | {224, 32+ch, 0, B} | B = (byte)((800 + 11\*d) / 19) | {224, 16, 66, (byte)(0x10<<ch)} | 1 |
| Uni SL-Infinity 0xA102 (SLI) | 0xA102 | {224, 32+ch, 0, B} | B = (byte)((200 + 19\*d) / 21) | {224, 16, 98, (byte)(0x10<<ch)} | 1 |
| Uni SL v2 0xA103/0xA105 (SLV2) | 0xA103, 0xA105 | {224, 32+ch, 0, B} | B = (byte)((250 + (17.5\*d)) / 20) | {224, 16, 98, (byte)(0x10<<ch)} | 2 |
| Uni AL v2 0xA104 (ALV2) | 0xA104 | {224, 32+ch, 0, B} | B = (byte)((250 + (17.5\*d)) / 20) | {224, 16, 98, (byte)(0x10<<ch)} | 2 |

Notation: `ch` is the zero-based channel index, `d` is the requested duty (0 to 100), and `B` is the computed speed byte. `<<` is a left shift. The casts to `byte` are the integer truncation that actually ships on the wire.

## Set-speed report

Every family uses the same four-byte set-speed report shape:

```
{ 224, 32 + ch, 0, B }
```

The third byte is always `0`. Only the duty-to-byte formula differs by family (see the matrix). The duty `d` is clamped to 0..100 before the formula is applied.

### Zero duty is a full-stop request

`d = 0` is a special case: it emits speed byte `B = 0`, **not** the formula result. The per-family formulas never fall to zero - their lowest running step (`d = 1`) is byte `42` for SL/AL, `10` for SLI, `13` for SLV2/ALV2, the lowest _reliable spin_ speed for a running fan - so feeding a commanded `0%` through the formula would idle the fan near that minimum instead of letting it stop. Emitting byte `0` mirrors L-Connect, whose fan-curve logic computes `Math.Max(speed, 0)` and sends a plain `SetFanSpeed(0)` at the bottom of the curve; there is no separate "stop" command in the protocol.

Whether the fan actually reaches 0 rpm is then up to the controller firmware:

- Controllers that support zero-rpm / start-stop honor byte `0` and stop the fan.
- The **SL-Infinity** (`0xA102`) firmware clamps a sub-floor byte up to its ~210 rpm minimum and does **not** truly stop under host duty control. This is a hardware limitation (verified on real hardware); L-Connect's start-stop has the same limitation on this family, because it is software-driven (the curve computes `0` and sends byte `0`), not a firmware mode.

The plugin faithfully encodes whatever duty the host requests and never floors `0%` back to a minimum - a user who prefers a minimum spin over a stop sets a non-zero floor in their FanControl curve. When and whether `0%` is requested is the host's decision.

## Duty-to-byte formulas

The formulas below apply for `d` in 1..100; `d = 0` short-circuits to byte `0` (see above).

- SL / AL: `B = (byte)((800 + 11 * d) / 19)`
- SLI: `B = (byte)((200 + 19 * d) / 21)`
- SLV2 / ALV2: `B = (byte)((250 + (17.5 * d)) / 20)`

## Manual-mode report (disable the firmware PWM curve)

To take a channel away from the controller's built-in curve and hold it under host control, write:

```
{ 224, 16, reg, (byte)(0x10 << ch) }
```

with the per-family register byte `reg`:

- SL: `49`
- AL: `66`
- SLI / SLV2 / ALV2: `98`

## ARGB-sync report (ARGB build only)

ARGB sync is compiled in only when the plugin is built with the `ENABLE_ARGB` symbol (`-p:EnableArgb=true`), which ships as the separate `FanControl.LianLi.Argb.dll`. The standard build never emits it. When present, the controller is told once at startup to take its LED lighting from the motherboard's ARGB header (the same "ARGB header sync" toggle as Lian Li L-Connect / uni-sync). It is asserted with:

```
{ 224, 16, argbReg, 1, 0, 0, 0 }
```

with the per-family ARGB register byte `argbReg`:

- SL: `48`
- AL: `65`
- SLI / SLV2 / ALV2: `97`

Caveat: on controllers that do not persist lighting to hardware (e.g. UNI FAN SL-Infinity 120 V1), asserting this at every startup resets their lighting to factory defaults. That is why it is a separate, opt-in build rather than always on.

## RPM decode

RPM telemetry arrives in an input report with id `224` and length `65`. Each channel's tachometer is a 16-bit big-endian value:

```
rpm[ch] = (buf[off + ch*2] << 8) | buf[off + ch*2 + 1]
```

The base offset `off` into the buffer is family-dependent:

- SL / AL / SLI: `off = 1`
- SLV2 / ALV2: `off = 2`

## RPM validation

A decoded RPM is trusted only when it is `0..6000`. After a hibernate/power cycle the SL-Infinity returns its idle-state input buffer, which decodes to ~50000 rpm, and a read that races USB re-enumeration can return a partial buffer; the Uni fans top out around 2100 rpm (SL-Infinity) and no family exceeds ~3000, so any larger value is garbage. An implausible read is **ignored**: the previous good value for that channel is kept, and the onset is logged once (and recovery once), so a persistent garbage read is visible without spamming the log. This is the same robustness L-Connect gets from validating the report frame before trusting it, rather than decoding whatever bytes arrive. The bound lives in the pure `ChannelReadDecision` so it is testable in isolation.

## Channel byte

The manual-mode channel selector byte is `0x10 << ch`, so:

- ch0 -> `0x10`
- ch1 -> `0x20`
- ch2 -> `0x40`
- ch3 -> `0x80`

## Recorded upstream bugs we deliberately avoid

These are real defects observed in upstream and forked implementations. They are listed here so the encoders are never "simplified" back into them.

1. **Channel byte must be `0x10 << ch`.** Some code computes the channel selector as `(2 * ch) * 16`, which for ch3 yields `0x60` instead of the correct `0x80`. That silently addresses the wrong channel. The selector is a shift of a single bit, not an arithmetic scaling: ch3 is `0x80`.
2. **Do not swap the SLI and SLV2/ALV2 duty formulas.** SLI uses `(200 + 19*d) / 21`; SLV2 and ALV2 use `(250 + 17.5*d) / 20`. A liquidctl fork swapped these two formulas between the families. They are not interchangeable - applying the SLV2 formula to an SLI (or vice versa) produces wrong duty bytes across the whole range.

## Sources

The byte-level facts above were learned and cross-checked against prior open-source implementations of the same protocol. Only the protocol facts (report ids, offsets, and duty formulas) were reused; no source code was copied.

- uni-sync (https://github.com/EightB1ts/uni-sync) - Rust, MIT.
- FanControl.LianLi (https://github.com/EightB1ts/FanControl.LianLi) - the original FanControl plugin, LGPL-2.1.
- liquidctl (https://github.com/liquidctl/liquidctl) - GPL-3.0-or-later.

## Out of scope

These Lian Li products are intentionally NOT in this plugin's catalog. They use different protocols or different transports and are not supported here:

- Strimer L Connect - PID `0xA200`
- Universal Screen LED - PID `0x8050`
- Galahad II Trinity - a different vendor id, `0x0416`
