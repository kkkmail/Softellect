# VPN GATEWAY SPEC 041 — AUTH-NEGOTIATED AES FOR UDP PUSH

THIS SPEC IS AUTHORITATIVE.  
NO ALTERNATIVES. NO OPTIONS. NO LEGACY.  
IF SOMETHING IS MISSING → CC MUST STOP AND ASK.

---

## 1. SCOPE

THIS CHANGE INTRODUCES:

- ENCRYPTED + SIGNED AUTHENTICATION (WCF PLANE)
- FAST, LIGHTWEIGHT AES ENCRYPTION FOR UDP PUSH (DATA PLANE)
- NO PER-PACKET SIGNING
- NO INTEGRITY GUARANTEES FOR UDP (BY DESIGN)

THIS SPEC DOES **NOT** CHANGE:
- vpnTransportProtocol
- routing logic
- packet splitting / reassembly
- MTU logic beyond header adjustment

---

## 2. CONFIGURATION SEMANTICS (MANDATORY)

### appsettings.json (FLAT, UNDER appSettings)

- `useEncryption : bool`
  - APPLIES **ONLY TO UDP DATA PLANE**
- `encryptionType : EncryptionType`
  - APPLIES **ONLY TO AUTHENTICATION (WCF)**

`useEncryption = false`  
→ UDP payload is plaintext.

`useEncryption = true`  
→ UDP payload is AES-encrypted as defined below.

AUTHENTICATION IS **ALWAYS ENCRYPTED AND SIGNED**, REGARDLESS OF `useEncryption`.

---

## 3. AUTHENTICATION (WCF PLANE)

### REQUIRED CHANGES

- `VpnAuthRequest` and `VpnAuthResponse` MUST BE:
  - encrypted
  - signed
- Use existing:
  - `tryEncryptAndSign`
  - `tryDecryptAndVerify`
- Use `encryptionType` from appsettings.json.

### KEY EXCHANGE (MANDATORY)

DURING AUTHENTICATION:

- CLIENT AND SERVER EXCHANGE A **RANDOM AES SESSION KEY**
- THIS AES KEY IS:
  - generated during authentication
  - protected by the encrypted + signed WCF exchange
- EACH SIDE STORES THE AES KEY IN ITS **SESSION STATE**

NO NEW KEY LOADING LOGIC IS ALLOWED.  
USE EXISTING KEY MANAGEMENT.

---

## 4. UDP SESSION STATE (MANDATORY)

### EACH UDP SESSION STORES:

- `sessionId : byte`
  - VALUE RANGE: 1–255
  - VALUE 0 IS RESERVED FOR SERVER
- `sessionAesKey : byte[]`

SESSION LOOKUP IS DONE BY `sessionId`.

---

## 5. UDP DATAGRAM FORMAT (MANDATORY)

```
[ sessionId : 1 byte ][ nonce : 16 bytes GUID ][ payload... ]
```

- `sessionId` IS **ALWAYS UNENCRYPTED**
- `nonce` IS **ALWAYS UNENCRYPTED**
- `payload` IS:
  - plaintext if `useEncryption = false`
  - AES-encrypted if `useEncryption = true`

NO CLIENT ID IS PRESENT IN UDP DATAGRAMS.

---

## 6. AES KEY DERIVATION PER PACKET (MANDATORY)

FOR EACH UDP PACKET:

INPUTS:
- `sessionAesKey`
- `nonce (Guid)`

DERIVE A PER-PACKET `AesKey` **DETERMINISTICALLY** FROM:
```
(sessionAesKey, nonce)
```

THE DERIVATION MUST:
- PRODUCE:
  - AES key bytes
  - AES IV bytes
- BE SYMMETRIC (CLIENT AND SERVER MUST DERIVE IDENTICAL RESULTS)
- USE A CRYPTOGRAPHIC HASH / HMAC
- NOT USE XOR OR CONCATENATION DIRECTLY

THIS DERIVED `AesKey` IS USED **ONCE** FOR THAT PACKET.

---

## 7. UDP SEND / RECEIVE LOGIC

### SEND

1. Generate `nonce = Guid.NewGuid()`
2. Derive `AesKey` from `(sessionAesKey, nonce)`
3. If `useEncryption = true`:
   - encrypt payload with `tryEncryptAesKey`
4. Build datagram as:
   ```
   sessionId + nonce + payload
   ```

### RECEIVE

1. Read `sessionId`
2. Lookup session
3. Read `nonce`
4. Derive `AesKey` from `(sessionAesKey, nonce)`
5. If `useEncryption = true`:
   - decrypt payload with `tryDecryptAesKey`
6. Process plaintext payload

NO SIGNING. NO VERIFYING. NO FALLBACK.

---

## 8. FAILURE BEHAVIOR (MANDATORY)

### AUTHENTICATION

- ANY CRYPTO FAILURE → AUTH FAILS

### UDP (SERVER)

- AES DECRYPT FAILURE:
  - LOG ERROR ONCE
  - TERMINATE SESSION
  - STOP LOGGING FOR THAT CLIENT

### UDP (CLIENT)

- AES DECRYPT FAILURE:
  - LOG CRITICAL ERROR ONCE
  - IMMEDIATELY CRASH

---

## 9. PERFORMANCE INTENT

THIS DESIGN IS INTENDED TO:

- KEEP UDP OVERHEAD AT **17 BYTES**
- AVOID RSA OPERATIONS ON DATA PLANE
- AVOID MTU INFLATION (>1500)
- RUN ON WEAK SERVERS

---

## 10. STYLE RULES (MANDATORY)

- F# → camelCase
- C# → PascalCase
- NO COPY / PASTE
- NO LEGACY CODE
- NO “TEMPORARY” SOLUTIONS

---

## FINAL RULE

IF CC CANNOT FIND:
- EXISTING TYPES
- EXISTING FUNCTIONS
- EXISTING KEY MATERIAL

→ **STOP AND ASK.**
