# pintc-cs

C# implementation of the Pint compiler. Produces Win32 native binaries (x86 PE).

Pint is a minimal systems language: manual memory, strong static types, no runtime,
no hidden allocations. Designed for OS kernels, embedded firmware, and low-level systems work.

- **Compiler binary:** `pintc`
- **Language spec:** [pintc-docs](https://github.com/sqrt57/pintc-docs)

## Build & test

```bash
dotnet build          # build everything
dotnet test           # all tests
dotnet test Pintc.Tests      # unit and integration tests only
dotnet test Pintc.E2eTests   # end-to-end tests only (compile + run output EXE)
```

## License

MIT — see [LICENSE](LICENSE).
