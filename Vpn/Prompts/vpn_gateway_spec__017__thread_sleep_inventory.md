# vpn_gateway_spec__017__thread_sleep_inventory.md

## Goal

Create an **inventory of all `Thread.Sleep` call sites** in the VPN codebase.

- Repo root: `C:\GitHub\Softellect\Vpn\`
- Scope: **F# source files only** (`*.fs`, `*.fsi`, `*.fsx`)
- Search recursively
- **Do not write any code.** (No new/changed source files. No refactors. No fixes.)

## Output

Write the results to a **new Markdown file**:

- `vpn_gateway_spec__018__thread_sleep_inventory_results.md`

The output file must contain:

- **One line per `Thread.Sleep` call site**
- Each line must include:
  1) **Full module name** (and **class/type** if applicable)
  2) **Function / member** name containing the call
  3) **Very short purpose** of the sleep (e.g., *poll loop backoff*, *retry delay*, *rate limiting*, *wait for shutdown*, *startup stagger*, *work-queue polling*, *temporary mitigation*, etc.)
  4) **File path + line number** for the call site

### Required line format (strict)

Use exactly this pattern per call site:

`- <Module>[.<Type>] :: <function_or_member>  |  <purpose>  |  <relative_path>:<line>`

Examples (synthetic):
- `- Softellect.Vpn.Gateway.ExternalGateway :: sendLoop  |  queue polling backoff  |  src\Vpn\Gateway\ExternalGateway.fs:217`
- `- Softellect.Vpn.Dns.DnsProxy.DnsProxyAgent :: run  |  retry delay after socket error  |  src\Vpn\Dns\DnsProxy.fs:144`

If you are **not sure** about module/type/member, still emit the line, but put `?` for the unknown part (do not omit).

## Procedure

### 1) Find all `Thread.Sleep` occurrences

Search for:
- `Thread.Sleep`
- (also include any fully-qualified forms if present, e.g. `System.Threading.Thread.Sleep`)

Do this recursively under `C:\GitHub\Softellect\Vpn\` restricted to F# files.

You may use:
- `ripgrep` / `rg` if available
- `findstr` / PowerShell `Select-String`
- IDE “Find in Files”

But **do not** modify any files.

### 2) For each match, extract context

For every match:
- Capture the **file path and line number**
- Inspect surrounding lines to determine:
  - The **namespace/module chain**
  - The **type/class** (if inside `type ...` or class)
  - The **function/member** name containing the call:
    - `let <name> ...`
    - `let rec <name> ...`
    - `member _.<name> ...`
    - `static member <name> ...`
    - `override _.<name> ...`
  - A **very short purpose** for why the sleep exists (keep it under ~8 words)

### 3) Write results to `vpn_gateway_spec__018__thread_sleep_inventory_results.md`

- One bullet line per call site
- Keep ordering stable:
  - Sort by file path, then by line number

## Constraints / Guardrails

- **No source changes.**
- **No new code files.**
- Do not “suggest fixes” in the results file—**inventory only**.
- If a `Thread.Sleep` is in code that appears dead/unused, still include it (note purpose as e.g. `legacy / unused?`).

## Definition of “call site”

Count each distinct occurrence of `Thread.Sleep(...)` in the repository.  
If the same line is compiled under multiple conditional compilation symbols, still list it once (but you may note `#if` context briefly in the purpose if relevant).
