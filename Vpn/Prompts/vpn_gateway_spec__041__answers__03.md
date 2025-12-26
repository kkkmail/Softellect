# VPN GATEWAY SPEC 041 — AUTHORITATIVE ANSWERS (03)

THIS FILE CONTAINS A FINAL, AUTHORITATIVE CLARIFICATION  
REGARDING **AES KEY + IV DERIVATION USING HMAC-SHA256**.

THERE ARE **NO OPTIONS**, **NO ALTERNATIVES**, **NO ORs**.

---

## ISSUE CLARIFIED

HMAC-SHA256 PRODUCES **32 BYTES**.

THE SYSTEM REQUIRES:
- **32 BYTES** FOR AES KEY
- **16 BYTES** FOR AES IV
- **48 BYTES TOTAL**

THIS IS RESOLVED BY **RUNNING HMAC-SHA256 TWICE WITH DOMAIN SEPARATION**.

---

## DERIVATION RULE (MANDATORY)

### INPUTS

- `sessionAesKey : byte[]`
- `nonce : Guid` → `nonceBytes : byte[16]`

---

## EXACT DERIVATION (MANDATORY)

### STEP 1 — DERIVE AES KEY

```
keyMaterial = HMAC-SHA256(sessionAesKey, nonceBytes || 0x01)
```

- Output: **32 bytes**
- Used directly as:
  - `AesKey.key`

---

## STEP 2 — DERIVE AES IV

```
ivMaterial = HMAC-SHA256(sessionAesKey, nonceBytes || 0x02)
```

- Output: **32 bytes**
- Use:
  - `ivMaterial[0..15]` → **16 bytes**
  - As `AesKey.iv`

---

## FINAL AesKey CONSTRUCTION

```fsharp
{
    key = keyMaterial              // 32 bytes
    iv  = ivMaterial[0..15]        // 16 bytes
}
```

THIS `AesKey` IS USED **ONCE PER PACKET** WITH:
- `tryEncryptAesKey`
- `tryDecryptAesKey`

---

## FORBIDDEN (EXPLICIT)

- ❌ HMAC-SHA512
- ❌ HKDF
- ❌ Single HMAC reused or stretched
- ❌ XOR
- ❌ Raw SHA256
- ❌ Concatenation without domain separation

---

## RATIONALE (NON-NEGOTIABLE)

- Keeps **HMAC-SHA256** as specified
- Provides proper **domain separation**
- Deterministic and symmetric
- Minimal overhead
- Compatible with existing `AesKey` and AES helpers

---

## FINAL RULE

IF ANY PART OF THIS DERIVATION IS UNCLEAR OR CANNOT BE IMPLEMENTED  
WITH EXISTING HELPERS → **STOP AND ASK**.
