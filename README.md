# pintc-cs

C# implementation of the Pint compiler. Produces Win32 native binaries (x86 PE).

Pint is a minimal systems language: manual memory, strong static types, no runtime,
no hidden allocations. Designed for OS kernels, embedded firmware, and low-level systems work.

- **Compiler binary:** `pintc`
- **Language spec:** [pintc-docs](https://github.com/sqrt57/pintc-docs)

## Build & test

```bash
dotnet build                        # build everything
dotnet test                         # all tests
dotnet test Pintc.Tests             # unit tests (pure in-process)
dotnet test Pintc.IntegrationTests  # integration tests (compiler internals + OS)
dotnet test Pintc.E2eTests          # e2e tests (pintc as black box, run output EXE)
```

## License

MIT — see [LICENSE](LICENSE).
