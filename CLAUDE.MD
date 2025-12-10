# Softellect Project - AI Assistant Guide

## Project Overview

This is a collection of F# libraries for building distributed applications, with a focus on simplifying client-server communication and distributed processing using native F# types.

### Core Libraries

1. **Softellect.Sys** - Foundation library with core primitives, utilities, and error handling
2. **Softellect.Wcf** - F# wrapper around CoreWCF for type-safe client-server communication
3. **Softellect.Messaging** - Generic messaging system for exchanging strongly-typed F# structures
4. **Softellect.DistributedProcessing** - Map/reduce-style distributed computation framework
5. **Softellect.Math** - Mathematical utilities and operations
6. **Softellect.Analytics** - Analytics functionality

## Technology Stack

- **Language**: F# (.NET 9.0)
- **Communication**: CoreWCF
- **Serialization**: FsPickler (for binary serialization with compression)
- **Build**: .NET SDK with MSBuild
- **Testing**: F# test projects (MathTests, SysTests)

## Project Structure

```
/
├── Sys/                    # Core primitives and utilities
├── Wcf/                    # WCF wrapper library
├── Messaging/              # Messaging library
├── MessagingData/          # Messaging data models
├── MessagingService/       # Messaging service implementation
├── DistributedProcessing/  # Distributed processing framework
│   ├── Core/              # Core distributed processing logic
│   ├── ModelGenerator/    # Model generation utilities
│   ├── SolverRunner/      # Solver execution
│   ├── PartitionerService/ # Work partitioning service
│   ├── PartitionerAdm/     # Partitioner administration
│   ├── WorkerNodeService/  # Worker node service
│   └── WorkerNodeAdm/      # Worker node administration
├── Math/                   # Mathematical operations
├── Analytics/              # Analytics functionality
├── Apps/                   # Application implementations
│   └── DistrProc/         # Distributed processing applications
├── Samples/                # Example projects
│   ├── Wcf/               # WCF examples (client, service, NetCore port)
│   ├── Msg/               # Messaging examples (clients, service)
│   └── DistrProc/         # Distributed processing examples
├── Scripts/                # Utility scripts
└── Tests/                  # Test projects (SysTests, MathTests)
```

## Key Concepts

### 1. F#-First Design
- All APIs use native F# types (records, discriminated unions)
- Immutable data structures throughout
- Railway-oriented programming (Result types) for error handling

### 2. Communication Architecture
- **High-level interface**: Natural F# types and structures
- **Low-level interface**: FsPickler serialization → compression → byte array transport
- Achieves ~100x size reduction compared to JSON/XML for large structures (150MB+)

### 3. Distributed Processing
- Map/reduce pattern where map operations are computationally expensive
- Reduce operations are near-instantaneous
- Services: Partitioner (distributes work) and WorkerNodes (execute work)

## Important Files

### Configuration
- `global.json` - .NET SDK version configuration
- `log4net.config` - Logging configuration
- `*.sln` / `*.slnx` - Solution files (SoftellectMain, SoftellectMigrations)

### Build Scripts
- `copyPackages.bat` - Package copying utility
- `CleanAll.ps1` - Clean all build artifacts
- `GetFolderSizes.ps1` - Analyze folder sizes
- `Sys/IncrementBuildNumber.fsx` - Build number management
- `Sys/BuildInfo.ps1` - Build information script

### Core Source Files
- `Sys/Core.fs` - Core utilities and primitives
- `Sys/Primitives.fs` - Basic type definitions
- `Sys/Rop.fs` - Railway-oriented programming utilities
- `Sys/Errors.fs` - Error handling
- `Sys/AppSettings.fs` - Application configuration
- `Sys/Logging.fs` - Logging infrastructure

## Development Guidelines

### When Making Changes

1. **Preserve F# Idioms**: Keep functional programming style, immutability, and type safety
2. **Follow Existing Patterns**: Use Result types for error handling, composition over inheritance
3. **Update Version Info**: Respect versioning in VersionInfo.fs files
4. **Test**: Run relevant test projects (SysTests, MathTests) after changes
5. **Dependencies**: Check project references in .fsproj files before adding new dependencies

### Common Tasks

#### Building the Solution
```bash
dotnet build SoftellectMain.sln
```

#### Running Tests
```bash
dotnet test SysTests/SysTests.fsproj
dotnet test MathTests/MathTests.fsproj
```

#### Working with Services
- Each service has a paired administration tool (e.g., PartitionerService + PartitionerAdm)
- Services use Softellect.Wcf for communication
- Configuration is typically in appsettings.json or via AppSettings.fs

### Code Style

- **File Organization**: Type definitions at top, functions below
- **Naming**: PascalCase for types, camelCase for values/functions
- **Error Handling**: Use Result<'T, 'E> or custom error types from Errors.fs
- **Async**: Prefer async computation expressions
- **Modules**: Organize related functionality in modules

## Architecture Patterns

### Service Architecture
```
Client → Proxy → [Network/WCF] → ServiceProxy → Service Implementation
```

### Messaging Flow
```
Client A → MessagingService ← Client B
    ↓           ↓           ↑
   Store    Route/Queue   Retrieve
```

### Distributed Processing Flow
```
ModelGenerator → PartitionerService → WorkerNodes
                      ↓                    ↓
                  Partition Work      Execute Tasks
                      ↓                    ↓
                  Collect Results ← Return Results
```

## Dependencies

Key NuGet packages used:
- CoreWCF (WCF implementation)
- FsPickler (F# serialization)
- FSharp.Core
- log4net (logging)
- Various .NET libraries for data access, crypto, etc.

## Troubleshooting

### Common Issues
1. **Build Errors**: Check .NET SDK version matches global.json
2. **Service Communication**: Verify WCF endpoints in configuration
3. **Serialization**: Ensure types are properly decorated for FsPickler
4. **Version Mismatch**: Check VersionInfo.fs in each project

### Debugging
- Enable detailed logging in log4net.config
- Check service logs for communication issues
- Use WcfServiceInfo/MsgServiceInfo tools for diagnostics

## Testing Strategy

- **Unit Tests**: SysTests for Sys library, MathTests for Math library
- **Integration**: Sample projects demonstrate end-to-end scenarios
- **Manual**: Use Admin tools to test service functionality

## Version Information

Current version line: 9.0.300.x (based on recent commits)
Build number is managed via IncrementBuildNumber.fsx script

## Notes for AI Assistants

- This codebase values **simplicity and correctness** over cleverness
- **Type safety is paramount** - leverage F#'s type system
- Changes should maintain **backward compatibility** where possible
- When adding features, follow the **established patterns** in similar components
- Always consider the **distributed nature** - changes may affect multiple services
- **Documentation in code** is preferred over external documentation
- The project uses **functional composition** - avoid adding unnecessary abstractions
