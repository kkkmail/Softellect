# F# Naming Conventions

This document extracts and formalizes the **updated F# naming conventions** used across Softellect projects.

---

## 1. Core Suffixes

### Param
**Purpose:**  
Tuples or records holding a collection of parameters, typically for functions or configuration.

**Role:** Primary source from which data is created or manipulated.

**Example:**
```fs
type DbConnectionParam = { server : string; port : int }
```

---

### Data
**Purpose:**  
Records, discriminated unions, or complex data types.

**Role:** Secondary; usually derived or modified based on parameters.

**Example:**
```fs
type CustomerData = { name : string; age : int }
```

---

### Info
**Purpose:**  
Metadata or descriptive information about other elements.

**Example:**
```fs
type AuthorInfo = { name : string; email : string }
```

---

### State
**Purpose:**  
Represents the mutable or observable state of a system or object.

**Example:**
```fs
type GameState = { position : int * int; score : int }
```

---

### FuncValue
**Purpose:**  
Discriminated Unions that map to functions.

**Reason:**  
Allows serialization of behavior and later mapping back to executable functions.

**Example:**
```fs
type MapperFuncValue =
    | AddOne
    | MultiplyByTwo
```

---

### Delegate
**Purpose:**  
Collection (record) of functions passed together.

**Notes:**  
Preferred over ambiguous functional groupings when intent is execution.

**Example:**
```fs
type MathDelegate =
    { add : int -> int -> int
      multiply : int -> int -> int }
```

---

### Generator
**Purpose:**  
A concise synonym for `Delegate` when the intent is *generation*.

**Example:**
```fs
type ResultGenerator =
    { generate : Input -> Output }
```

---

### Context
**Purpose:**  
Groups both data and functions.

**Typical Use:**  
Database contexts, service environments.

**Example:**
```fs
type DbContext =
    { data : CustomerData
      saveChanges : unit -> int }
```

---

### Proxy
**Purpose:**  
Third‑party or remote communication boundaries.

**Typical Use:**  
HTTP, WCF, RPC, or machine‑boundary interactions.

**Example:**
```fs
type HttpProxy =
    { address : string
      port : int }
```

---

### Builder
**Purpose:**  
Responsible for constructing or assembling complex objects.

**Example:**
```fs
type ReportBuilder =
    { build : ConfigParam -> Report }
```

---

### Factory
**Purpose:**  
Dynamically creates instances based on parameters or configuration.

**Example:**
```fs
type ServiceFactory =
    { create : ConfigParam -> Service }
```

---

### Resolver
**Purpose:**  
Resolves or determines dependencies or configuration.

**Example:**
```fs
type DependencyResolver =
    { resolve : string -> Dependency }
```

---

### Orchestrator
**Purpose:**  
Coordinates multiple components or actions.

**Example:**
```fs
type TaskOrchestrator =
    { orchestrate : Task -> Result }
```

---

### Coordinator
**Purpose:**  
Organizes workflows, events, or actions.

**Example:**
```fs
type EventCoordinator =
    { coordinate : Event -> Outcome }
```

---

### Provider
**Purpose:**  
Supplies resources, services, or data.

**Example:**
```fs
type DataProvider =
    { provide : Query -> Data }
```

---

## 2. Field‑Level Suffixes

### Opt
**Purpose:**  
Indicates optional fields for readability.

**Example:**
```fs
type CustomerData =
    { name : string
      ageOpt : int option }
```

---

## 3. General Rules

- **Collections:**  
  Use plural names instead of suffixes.
  ```fs
  let customers : CustomerData list
  ```

- **Functions:**  
  Use verbs or actions (`add`, `compute`, `resolve`).

- **Mutable values:**  
  Rare; no `Var` suffix required.

- **Option‑returning functions:**  
  Encode in the name, not a suffix.
  ```fs
  let tryFindCustomer id = ...
  ```

---

## 4. Summary Table

| Suffix        | Meaning |
|---------------|--------|
| Param         | Input parameters |
| Data          | Domain data |
| Info          | Metadata |
| State         | Runtime state |
| FuncValue     | Serializable behavior |
| Delegate      | Function collection |
| Generator     | Producing behavior |
| Context       | Data + functions |
| Proxy         | Remote / boundary IO |
| Builder       | Object construction |
| Factory       | Dynamic creation |
| Resolver      | Dependency lookup |
| Orchestrator  | High‑level coordination |
| Coordinator   | Workflow management |
| Provider      | Resource supplier |

---

**Authoritative reference for Softellect F# codebases.**
