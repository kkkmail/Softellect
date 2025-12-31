# vpn_gateway_spec__054__android16_fix_missing_middle_band_layout.md

## Goal

Fix the Android UI so that on **Android 16** (and earlier), the UI always shows:

1. **Band 1 (top):** bluish title band with text **“Softellect VPN”**
2. **Band 2 (middle):** Connect button + status (this is the band that currently disappears on Android 16)
3. **Band 3 (bottom):** info/log area (this is the only band allowed to shrink)

**Bands 1 and 2 must always remain visible.**  
Only Band 3 may shrink/scroll.

This must be implemented with the simplest, most stable Android view layout possible.

---

## Scope

Project:

- `C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\`

Primary file:

- `C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\MainActivity.fs`

---

## Constraints

1. **Do not attempt to build or run** the project (static changes only).
2. **Minimal change set**:
   - Only change what is required to keep Bands 1 and 2 visible on Android 16.
   - Do not refactor unrelated code.
3. **No “options” in implementation**:
   - Implement one clear solution.
4. Preserve existing look & feel as much as possible:
   - Keep the three-band visual structure.
   - Keep existing colors/text as-is unless required for layout correctness.

---

## Required behavior

### Layout invariants

- Band 1 and Band 2 must be laid out with **non-zero height** and must not be collapsible by weight/measurement.
- Band 3 must be the only area that:
   - expands to fill the remaining space, and
   - shrinks when space is reduced, and
   - scrolls if content exceeds available space.

### Android 16 compatibility

Android 15/16 edge-to-edge behavior can cause content to be measured/clipped differently. Ensure the top of the content is not hidden under system bars by applying a minimal **top inset / safe-area padding** to the root content container so Bands 1 and 2 remain visible.

This must be done in the simplest possible way:
- apply system top inset once to the root container padding (or equivalent),
- do not introduce complex UI frameworks or additional screens.

---

## Implementation requirements (authoritative)

CC must restructure the layout creation so it is explicitly:

### Root container
- A vertical container that owns all three bands.
- Bands 1 and 2 are added first and must be **wrap-content height** (or fixed dp) and **not weight-based**.
- Band 3 is added last and must be **the only child that fills remaining space**.

### Band 1 (Top title band)
- Must remain visible always.
- Must NOT use any layout weight.
- Must NOT use height=0.
- Must be added directly to root.

### Band 2 (Connect/status band)
- Must remain visible always.
- Must NOT use any layout weight.
- Must NOT use height=0.
- Must be added directly to root, below Band 1.

### Band 3 (Info/log area)
- Must be the only flexible region.
- If it is a ScrollView/ListView/etc. it must be configured to:
  - take remaining height, and
  - scroll rather than forcing Bands 1/2 off-screen.

### Insets / safe-area padding (minimal)
- Add minimal handling so the root container (or an outer wrapper) applies the system top inset as extra top padding.
- Keep existing padding values and add inset on top of them (do not remove existing padding).
- The result must keep Band 1 visible and not covered by the status bar/cutout on Android 16.

---

## “Definition of done” checks (for user)

After CC changes, user will test:

- On Android 16: Band 1 visible, Band 2 visible, Band 3 present (may be smaller, but scrollable).
- On Android 13: layout unchanged (Bands 1/2/3 visible).
- Rotating screen or changing window size must not make Band 2 disappear.

---

## Deliverables

CC must provide:

1. A concise summary of what was changed and why Band 2 disappeared previously (layout/measurement reason).
2. A list of modified files (expect `MainActivity.fs`, and possibly theme/manifest only if required for inset handling).
3. No unrelated refactors.

---

## Prohibited

- Do not delete Band 1.
- Do not merge Bands 1 and 2.
- Do not move the Connect button into Band 3.
- Do not “solve” by removing logs/info entirely.
- Do not add new UI frameworks (Compose, etc.).
- Do not add a second Activity/screen.

---

## Output

Implement the fix directly in the repo under:

- `C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\`
