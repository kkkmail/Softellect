# VPN GATEWAY SPEC 040 — AUTHORITATIVE ANSWERS (01)

THIS FILE CONTAINS **FINAL, AUTHORITATIVE ANSWERS** TO CC QUESTIONS.  
THERE ARE **NO OPTIONS**, **NO ALTERNATIVES**, AND **NO ORs**.

CC MUST FOLLOW THIS EXACTLY.

---

## 1. DATA MODEL CORRECTION (MANDATORY)

PASSING MULTIPLE PARAMETERS FOR ENCRYPTION/KEYS IS **FORBIDDEN**.

### REQUIRED ACTION

- **MOVE `VpnClientServiceData`** so that it lives **NEXT TO**:
  - `VpnClientAccessInfo`
  - `VpnServerData`

This is the SAME structural level where `VpnServerData` already exists.

### INTENT

`VpnClientServiceData` IS THE **SINGLE SOURCE OF TRUTH** FOR:
- client access info
- encryption configuration
- cryptographic keys

ANY COMPONENT THAT NEEDS CLIENT RUNTIME DATA MUST TAKE **ONE PARAMETER**:
```
VpnClientServiceData
```

---

## 2. VpnPushUdpClient CONSTRUCTOR (MANDATORY)

### REQUIRED ACTION

- CHANGE `VpnPushUdpClient` CONSTRUCTOR TO TAKE:
```
VpnClientServiceData
```

### FORBIDDEN

- Passing:
  - clientPrivateKey
  - serverPublicKey
  - useEncryption
  - encryptionType
  as separate parameters

### ACCESS PATTERN INSIDE VpnPushUdpClient

All values MUST be accessed via:
- `data.clientAccessInfo`
- `data.useEncryption`
- `data.encryptionType`
- `data.clientPrivateKey`
- `data.serverPublicKey`

---

## 3. CLIENT CONFIG LOADING (MANDATORY)

### appsettings.json (FLAT, UNDER appSettings)

The following keys MUST exist logically but MAY be absent physically:

- `UseEncryption`
- `EncryptionType`

### LOAD RULES

- Loading MUST NOT FAIL if keys are missing.
- Defaults:
  - `useEncryption = false`
  - `encryptionType = EncryptionType.defaultValue` (AES)

These values MUST be loaded into:
- `VpnClientAccessInfo`
- then composed into `VpnClientServiceData`

---

## 4. SERVER CONFIGURATION (MANDATORY)

### VpnClientData (SERVER SIDE)

ADD:
- `useEncryption : bool`
- `encryptionType : EncryptionType`

These values MUST be:
- parsed from flattened appsettings representation
- propagated into `PushClientSession`

---

## 5. PushClientSession (MANDATORY)

ADD THE FOLLOWING FIELDS:
- `useEncryption`
- `encryptionType`

POPULATE THEM IN:
- `createPushSession`
- FROM `VpnClientData`

Encryption behavior is **PER CLIENT**, NOT GLOBAL.

---

## 6. KEY USAGE (NO REIMPLEMENTATION)

### EXISTING LOGIC MUST BE USED AS-IS

- Private key handling:
  - `Vpn/Core/KeyManagement.fs`
- Public key handling:
  - `Sys/Crypto.fs`

### ENCRYPTION FUNCTIONS (EXACT USAGE)

- Client → Server:
  - `tryEncryptAndSign encryptionType payload clientPrivateKey serverPublicKey`
  - `tryDecryptAndVerify encryptionType encryptedPayload serverPrivateKey clientPublicKey`

- Server → Client:
  - `tryEncryptAndSign encryptionType payload serverPrivateKey clientPublicKey`
  - `tryDecryptAndVerify encryptionType encryptedPayload clientPrivateKey serverPublicKey`

CC MUST NOT:
- create new crypto helpers
- reload keys differently
- duplicate key-loading logic

IF SOMETHING IS NOT FOUND → **ASK**.

---

## 7. UDP DATAGRAM FORMAT (MANDATORY)

### FINAL WIRE FORMAT

```
[ 16 bytes clientId GUID ][ payload... ]
```

- clientId is **ALWAYS UNENCRYPTED**
- payload is:
  - plaintext if `useEncryption = false`
  - encrypted+signed if `useEncryption = true`

THIS FORMAT IS USED:
- client → server
- server → client

---

## 8. RECEIVE PATH LOGIC (MANDATORY)

### SERVER RECEIVE

1. Read first 16 bytes → clientId
2. Lookup `PushClientSession`
3. IF `session.useEncryption = true`:
   - decrypt+verify remainder using:
     - recipientPrivateKey = server private key
     - senderPublicKey = client public key
4. ELSE:
   - treat remainder as plaintext
5. Parse UDP protocol payload

---

## 9. FAILURE BEHAVIOR (MANDATORY)

### SERVER

- On encryption mismatch OR decrypt/verify failure:
  - Log **ONE ERROR**
  - Remove client session
  - STOP processing packets for that client
  - NO LOG FLOODING

### CLIENT

- On decrypt/verify failure when encryption is expected:
  - Log **CRITICAL ERROR ONCE**
  - IMMEDIATELY CRASH

---

## 10. STYLE RULES (MANDATORY)

- **F#**: camelCase everywhere
- **C#**: PascalCase everywhere
- NO COPY/PASTE
- NO LEGACY
- NO “TEMPORARY” WORKAROUNDS

---

## FINAL NOTE

THIS FILE OVERRIDES ALL PREVIOUS DISCUSSION.  
IF ANY REQUIRED SYMBOL / FUNCTION / FIELD CANNOT BE FOUND → **STOP AND ASK**.
