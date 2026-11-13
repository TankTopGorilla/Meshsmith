# Meshsmith

An offline **STL → STEP / IGES** converter for mechanical parts. It reads a triangle mesh, finds the
analytic surfaces inside it (planes, cylinders, cones), trims and sews them into a solid, and writes an
editable STEP AP214 or IGES 5.3 file.

No network, no server, no account. The same input gives the same output, and every export is read back
from disk and checked before the app calls it a success.

![Meshsmith converting pin.stl: 274 triangles become a 4-face valid solid — 2 planes, 1 cylinder, 1 cone, in 0.52 s](docs/images/meshsmith-pin.png)

## Status

- **0.1.4**, Windows desktop app (WPF, .NET 8) with a headless CLI and an isolated conversion worker.
- **6 of 6** real test parts convert to a valid solid; 0 crashes.
- `Meshsmith.Core` 54 tests, `Meshsmith.App` 140 tests.

## Requirements

- Windows 10 or 11, 64-bit
- .NET 8 Desktop Runtime (the app is framework-dependent)

## Build

```bash
dotnet build Meshsmith.slnx
dotnet test
```

Run the app: `src/Meshsmith.App/bin/Debug/net8.0-windows/Meshsmith.App.exe`
Run the CLI: `src/Meshsmith.Cli/bin/Debug/net8.0-windows7.0/Meshsmith.Cli.exe convert part.stl -o part.step`

The OCCT native payload is copied next to the executables by the build; nothing else needs installing.

## How it works

1. **Read and weld** the STL into a shared-vertex mesh, then repair it (degenerate triangles, exact
   duplicate faces, winding, holes on manifold surfaces). Every change is counted and reported.
2. **Classify** the mesh into regions and fit a primitive to each (plane, cylinder, cone).
3. **Build** trimmed OCCT faces for the regions and **sew** them into a solid. If the analytic body does
   not close, the exact faceted solid is exported instead and the reason is reported.
4. **Write** STEP and IGES, then **read the file back** and compare face count, volume and validity.
   The checked file decides whether the conversion succeeded.
5. On any failure, write a diagnostic bundle (`%TEMP%\meshsmith-diag-*.zip`: error text, model, log
   tail) and show it on the error card.

The conversion runs in a separate worker process, so an OCCT crash cannot take the window down.

## Project layout

```
src/Meshsmith.Core     mesh, fitting, B-Rep building, exporters, verification
src/Meshsmith.App      WPF shell, view models, services
src/Meshsmith.Worker   the child process that owns the pipeline
src/Meshsmith.Cli      headless conversion
tests/                 Core and App test suites
fixtures/              small STL fixtures used by the tests
tools/                 corpus runner and other development scripts
HANDBOOK.md            engineering notes: pipeline, lessons, measurements
```

## Known limits

- The STL is read before the busy state appears; cancelling during that read is not possible yet.
- A part that needs the faceted fallback produces a large STEP (one face per triangle) and its surfaces
  are not editable as cylinders.
- Models above ~100k triangles are outside the tested envelope (they work, but slowly and with a large
  memory footprint).
- One conversion at a time; STEP AP214 and IGES 5.3 only.

## Third-party

- [OCCT](https://dev.opencascade.org/) via [Occt.NET](https://www.nuget.org/packages/Occt.NET) — LGPL 2.1
  with exception; shipped as separate native libraries.
- [HelixToolkit.Wpf](https://github.com/helix-toolkit/helix-toolkit) — MIT.

## License

**Source-available, free to use — not free to sell.** MIT, plus the
[Commons Clause](https://commonsclause.com/) condition:

- ✅ **Use it for anything, including commercial work.** Run it in your shop, in your company, on
  client parts, in your own products; modify it for your own use.
- ✅ **Share it**, with the licence attached.
- ❌ **Don't sell it** — not the app, not a bundle containing it, and not a paid service whose value
  derives substantially from Meshsmith itself.

That last line is the whole point: this is a tool for people who make things, not a product for
someone else to resell. It is *source-available*, not OSI "open source" — the phrase means exactly this
kind of restriction.

Third-party components keep their own licences (OCCT is LGPL 2.1 with exception; HelixToolkit is MIT)
and are not covered by the above.
