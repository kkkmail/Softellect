# vpn_gateway_spec__055__android_ui_immediate_state_feedback.md

## Purpose

Improve Android VPN user experience by making the **Connect / Disconnect button respond immediately** to user clicks, **without changing any VPN logic**.

This change is **UI-only**.  
No service logic, no state machine logic, no networking logic may be modified.

---

## Problem Statement

Currently:

- When the user taps **Connect**, the button label changes immediately to `Connecting…` ✅
- When the user taps **Disconnect** (button label shows `Connected`), the UI **does not update immediately** ❌
- The UI appears frozen until the actual disconnection completes

This creates poor UX and the false impression that the app is hung.

---

## Required Behavior (Authoritative)

### Immediate UI feedback is mandatory

The button label **must change immediately on user click**, without waiting for the underlying connect / disconnect operation to complete.

---

## Button Label Rules

The button label must be derived from **two inputs**:
1. The **current VPN state** (already exists)
2. A **UI-only pending action flag** (new, UI layer only)

### Base VPN states → labels

- **Disconnected** → `Connect`
- **Connecting** → `Connecting…`
- **Connected** → `Connected`
- **Reconnecting** → `Reconnecting…`

---

## Immediate UI-only Overrides (Critical)

These overrides apply **immediately on button tap**.

### Disconnect requested

When the user taps the button while the VPN is in:
- **Connected**
- **Connecting**
- **Reconnecting**

Then:

- The button label **must immediately change** to **`Disconnecting…`**
- This happens synchronously on click
- No waiting for callbacks, flows, observers, or service responses

### Completion

- When the real VPN state later becomes **Disconnected**:
  - Clear the UI pending action
  - Button label becomes `Connect` (existing behavior)

---

## Implementation Constraints (Strict)

- ❌ Do NOT modify VPN logic
- ❌ Do NOT modify service lifecycle
- ❌ Do NOT modify networking, state machines, or threading
- ✅ Only UI / ViewModel / Compose state logic may be changed
- ✅ One simple UI-only pending state is sufficient (e.g. “pending disconnect”)

---

## Required Mental Model

This is an **optimistic UI update**:

- UI reflects *user intent immediately*
- Real VPN state eventually confirms or completes it
- UI reconciles automatically when the real state changes

This pattern is **already used for Connecting** — this change simply makes Disconnect symmetrical.

---

## Acceptance Criteria (Non-Negotiable)

1. Tapping Disconnect **always** changes the label to `Disconnecting…` immediately
2. There is **no visible UI stall**
3. Once disconnect completes, label becomes `Connect`
4. No functional behavior of the VPN changes
5. No new options, flags, or configuration knobs are introduced

---

## Contradictions Rule (Important)

If **any existing behavior** contradicts this specification  
(e.g. a state where tapping the button does *not* initiate disconnect):

👉 **STOP IMMEDIATELY**  
👉 **ASK FOR CLARIFICATION**  
👉 **DO NOT GUESS**  
👉 **DO NOT IMPLEMENT ALTERNATIVES**

---

## Scope Reminder

This spec is **UI-only**.  
Any logic change is a violation of this document.

---

**End of specification.**
