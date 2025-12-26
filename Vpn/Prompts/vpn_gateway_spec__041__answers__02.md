# VPN GATEWAY SPEC 041 — AUTHORITATIVE ANSWERS (02)

THIS FILE CONTAINS FINAL, AUTHORITATIVE ANSWERS TO CC QUESTIONS  
RELATED TO **SESSION ID, AES KEY GENERATION, AND KEY DERIVATION**.

THERE ARE **NO OPTIONS**, **NO ALTERNATIVES**, **NO ORs**.

---

## 1. SESSION ID ALLOCATION (MANDATORY)

- **THE SERVER ALLOCATES `sessionId`.**
- Type: `byte`
- Valid range: `1 .. 255`
- Value `0` is **RESERVED FOR SERVER**.

### AUTHENTICATION FLOW

- Server allocates `sessionId` during authentication.
- Server sends `sessionId` in `VpnAuthResponse`.
- Client stores `sessionId` in its session state.
- Client NEVER proposes or chooses a sessionId.

---

## 2. AES SESSION KEY GENERATION (MANDATORY)

- **THE SERVER GENERATES THE AES SESSION KEY.**
- Generation happens during authentication.
- The AES session key is sent to the client inside:
  - **encrypted + signed `VpnAuthResponse`**.

### CLIENT BEHAVIOR

- Client receives AES session key from auth response.
- Client stores it in its session state.
- Client NEVER generates or modifies the session key.

---

## 3. AES KEY REPRESENTATION (MANDATORY)

### EXISTING TYPE (MUST BE USED)

```fsharp
type AesKey =
    {
        key : byte[]
        iv  : byte[]
    }
```

### SESSION STORAGE RULE

- The **SESSION STORES A BASE AES KEY MATERIAL** (`byte[]`), NOT a per-packet `AesKey`.
- A per-packet `AesKey` is **DERIVED** from:
  - `sessionAesKey`
  - `nonce : Guid`

---

## 4. PER-PACKET AES KEY DERIVATION (MANDATORY)

### DERIVATION FUNCTION

- **USE HMAC-SHA256.**
- NO OTHER KDF IS ALLOWED.

### INPUTS

- `sessionAesKey : byte[]`
- `nonce : Guid` (16 bytes)

### PROCESS

1. Compute:
   ```
   hmac = HMAC-SHA256(sessionAesKey, nonceBytes)
   ```
2. Split `hmac` as:
   - First 32 bytes → `AesKey.key`
   - Next 16 bytes → `AesKey.iv`

3. Construct:
   ```fsharp
   let aesKey : AesKey =
       {
           key = keyBytes
           iv  = ivBytes
       }
   ```

### USAGE

- This derived `AesKey` is used **ONCE PER PACKET** with:
  - `tryEncryptAesKey`
  - `tryDecryptAesKey`

### FORBIDDEN

- HKDF
- raw SHA256
- XOR
- concatenation without HMAC

---

## 5. NONCE HANDLING (MANDATORY)

- Nonce type: `Guid`
- Size: 16 bytes
- Generation: `Guid.NewGuid()` **FOR EACH PACKET**
- Nonce is:
  - sent UNENCRYPTED
  - included in UDP header
- Nonce is NOT sequential.
- Nonce is NEVER reused.

---

## 6. UDP DATAGRAM HEADER (CONFIRMED)

```
[ sessionId : 1 byte ][ nonce : 16 bytes GUID ][ payload... ]
```

- Payload is:
  - plaintext if `useEncryption = false`
  - AES-encrypted if `useEncryption = true`

---

## 7. FAILURE BEHAVIOR (CONFIRMED)

### SERVER

- AES decrypt failure:
  - LOG ERROR ONCE
  - TERMINATE SESSION
  - STOP LOGGING FOR THAT CLIENT

### CLIENT

- AES decrypt failure:
  - LOG CRITICAL ERROR ONCE
  - IMMEDIATELY CRASH

---

## 8. FINAL RULE

CC MUST:
- USE EXISTING TYPES AND FUNCTIONS
- USE `AesKey` EXACTLY AS DEFINED
- NOT INTRODUCE NEW CRYPTO PRIMITIVES
- NOT GUESS OR INVENT

IF ANY REQUIRED SYMBOL CANNOT BE FOUND → **STOP AND ASK**.
