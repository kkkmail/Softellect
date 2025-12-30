# vpn_gateway_spec__049__android_power_button_vector_drawable.md

## Goal

Fix inconsistent / incorrect rendering of the Unicode power symbol (`⏻`) on Android by replacing the text glyph with a **vector drawable** power icon that renders consistently across devices and screen densities.

The current code uses:

- `C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\MainActivity.fs`
- `CreateTopControlRow(...)`
- `powerButton.Text <- "⏻"`

This must be replaced with a drawable-backed icon button.

## Non-goals

- No behavior changes to VPN logic.
- No layout redesign beyond what’s necessary to swap text → icon.
- No dependency-heavy icon packs unless already in use.

## Implementation plan

### 1) Add a vector drawable resource

Add a new vector drawable:

- **File**: `C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\Resources\drawable\ic_power.xml`
- **Purpose**: Provide a stable, resolution-independent power icon.

Use Material-style “power settings new” glyph path (safe, standard). Contents:

```xml
<?xml version="1.0" encoding="utf-8"?>
<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="24dp"
    android:height="24dp"
    android:viewportWidth="24"
    android:viewportHeight="24">
  <path
      android:fillColor="#FF000000"
      android:pathData="M13,3h-2v10h2V3zM17.83,5.17l-1.42,1.42C17.99,7.83 19,9.83 19,12c0,3.87 -3.13,7 -7,7s-7,-3.13 -7,-7c0,-2.17 1.01,-4.17 2.59,-5.41L7.17,5.17C5.23,6.82 4,9.26 4,12c0,4.42 3.58,8 8,8s8,-3.58 8,-8c0,-2.74 -1.23,-5.18 -3.17,-6.83z"/>
</vector>
```

Notes:
- Keep `fillColor` black; we’ll tint it from code to match the current theme/text color.

### 2) Replace the text glyph with an icon button in `CreateTopControlRow`

**File**: `C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\MainActivity.fs`

In `CreateTopControlRow(...)`, locate where `powerButton` is created/configured and remove:

- `powerButton.Text <- "⏻"`

Then implement one of the following (prefer A):

#### A) If `powerButton` can become an ImageButton (preferred)

Replace the existing `Button` with an `AppCompatImageButton` (or `ImageButton` if AndroidX AppCompat is not used).

Requirements:
- Set image resource to `ic_power`.
- Apply the same click handler currently used by `powerButton`.
- Ensure it visually behaves like a tappable control (ripple/selectable background).
- Tint the icon to match the old button’s text color (or a theme color).

Implementation details:
- Use `AndroidX.AppCompat.Widget.AppCompatImageButton` if available.
- Set:
  - `SetImageResource(Resource.Drawable.ic_power)`
  - `ContentDescription` to something like `"Power"` (or localized if strings exist)
  - Background: use selectable borderless background so it looks like an icon control in a toolbar/top row.
    - Resolve `?attr/selectableItemBackgroundBorderless` and set as background.
- Set padding similar to other controls in the row (match size/spacing).

Tinting:
- Read the current text color you would have used for the button (or the current theme’s `colorOnSurface` / `colorControlNormal`) and apply:
  - `SetColorFilter(color, PorterDuff.Mode.SrcIn)` (simple, compatible)

#### B) If `powerButton` must remain a normal Button

Keep the `Button`, but replace its text with a drawable:
- Set `powerButton.Text <- ""`
- Set a left (or top) compound drawable to `ic_power`
- Tint the drawable to match existing button text color

This is less clean visually (padding/alignment can be finicky) but avoids changing control type.

### 3) Keep layout stable

The top control row should keep the same overall height/alignment as before.

If switching to `ImageButton` changes sizing:
- Explicitly set `LayoutParameters` width/height to match the prior `powerButton` sizing, or:
  - Use `WrapContent` with padding tuned to match neighbors.

### 4) Acceptance criteria

- The power icon looks correct on Android devices regardless of OEM font.
- No reliance on the Unicode `⏻` glyph remains.
- Clicking the icon triggers the same behavior as the old power button.
- Icon tint matches the app theme (not stuck as black on dark themes, etc.).
- No new runtime permissions or VPN behavior changes.

## Quick test checklist

- Launch app and confirm the top control row shows a power icon (not a square/tofu/misrendered glyph).
- Tap power icon:
  - Start/stop flow behaves exactly as before.
- Test on at least:
  - Light theme (icon visible)
  - Dark theme (icon visible, tinted appropriately)

## Notes / constraints

- Do not add external icon libraries. The vector drawable resource is sufficient.
- Keep changes limited to:
  - `Resources/drawable/ic_power.xml` (new)
  - `MainActivity.fs` (`CreateTopControlRow` changes only)
