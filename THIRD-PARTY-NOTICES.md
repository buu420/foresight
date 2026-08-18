# Third-Party Notices

Chrono Trigger Accessibility uses the following third-party components. Chrono Trigger and its assets are not included in the mod package.

## Prism 0.17.3

Prism, the Platform-agnostic Reader Interface for Speech and Messages, is distributed under the Mozilla Public License 2.0. The package contains the upstream `LICENSE` and `NOTICE` files alongside the reviewed 32-bit Windows binary.

- Upstream: <https://github.com/ethindp/prism>
- Pinned tag: `v0.17.3`
- Pinned source commit: `9911156998b52fee91fb2cb4f71ac793d4e546c7`
- Packaged `prism.dll` SHA-256: `6A84322E42D1B4123E2E66E9887CFDF0CDEA2A972FA40FC7B7185AEC77F5178A`

Prism's upstream `NOTICE` identifies bundled or incorporated work including simdutf, NVDA controller RPC definitions, Moderncom, dr_wav, Djinni, concurrentqueue, and fmt. The notice and exact upstream `LICENSES` subtree from the pinned source commit are reproduced unmodified in the package, including each bundled component's license and the Moderncom authors file.

## Reloaded-II 1.30.3 (redistributed)

This mod no longer depends on a separately installed Reloaded-II. It vendors and deploys a trimmed, **loader-only** portion of Reloaded-II — no launcher GUI, no updater, no NuGet client — as a portable tree inside the game folder.

- Upstream: <https://github.com/Reloaded-Project/Reloaded-II>
- Version: 1.30.3
- License: GNU General Public License version 3
- Copyright: Sewer56
- Vendored at: `native/reloaded-ii/v1.30.3/Loader/X86/`
- Provenance and per-file SHA-256: `native/reloaded-ii/v1.30.3/SOURCE.md`
- Deployed package license copy: `LICENSES/GNU-GPL-3.0.txt`

The vendored binaries are **unmodified upstream builds**. Nothing was patched, recompiled, or converted between architectures. Only the 32-bit (`x86`) loader is redistributed, because Chrono Trigger is a 32-bit process. Corresponding source remains available from the upstream repository.

## Reloaded II Shared Lib: Reloaded.Hooks 1.16.3 (redistributed)

The hook implementation the mod depends on at runtime, redistributed as a Reloaded mod because there is no launcher to resolve mod dependencies.

- Upstream: <https://github.com/Sewer56/Reloaded.SharedLib.Hooks.ReloadedII>
- Version: 1.16.3
- License: GNU Lesser General Public License version 3
- Copyright: Sewer56
- Vendored at: `native/reloaded-ii/v1.30.3/mods/reloaded.sharedlib.hooks/`
- Deployed package license copy: `LICENSES/Reloaded.SharedLib.Hooks-LGPL-3.0.txt`

This is a *universal* Reloaded mod and legitimately ships both `x86` and `x64` payloads. Its x64 files are expected and are not stripped, even though only the x86 payload is loaded here.

## Reloaded-II interfaces and hooks (compile-time)

- Reloaded.Mod.Interfaces 2.5.0: GNU General Public License version 3. Used as a loader-provided interface; its assembly is not copied into the mod package.
- Reloaded.Hooks.Definitions 1.15.0: GNU Lesser General Public License version 3; <https://github.com/Reloaded-Project/Reloaded.Hooks>
- Reloaded.SharedLib.Hooks 1.9.0 compile-time interfaces: GNU Lesser General Public License version 3. The package includes `Reloaded.Hooks.ReloadedII.Interfaces.dll`; the working implementation comes from the vendored `reloaded.sharedlib.hooks` mod described above.

The package's `LICENSES` directory includes the GPL 3.0 text and the exact LGPL 3.0 license files shipped by the pinned Reloaded NuGet packages. No Reloaded binary has been modified by this project.

## Ultimate ASI Loader — no longer deployed

Earlier versions deployed 32-bit Ultimate ASI Loader 6.9.0 as `winmm.dll` next to the game so a normal Steam launch would load Reloaded. That is **superseded**: the native launcher in `src/ChronoTriggerAccessibility.Launcher` now injects the Reloaded bootstrapper directly, and deployment removes the proxy DLL.

The reviewed binary and its licence are retained in the repository for provenance and for anyone auditing an older installation.

- Upstream: <https://github.com/ThirteenAG/Ultimate-ASI-Loader>
- License: MIT
- Copyright: 2023 ThirteenAG
- Reviewed binary SHA-256: `A51C630B2EA3D78AD55A330EA64D510C8C0737F620BE65AD7503B61840D59E37`
- Repository license copy: `native/ultimate-asi-loader/v6.9.0/LICENSE`
- Deployed package license copy: `LICENSES/Ultimate-ASI-Loader-MIT.txt`

## Microsoft .NET

A 32-bit Microsoft .NET 9 runtime is an external prerequisite and is not included in the mod package. Its licensing remains governed by Microsoft.

No patch version is pinned. The Reloaded loader's `runtimeconfig.json` requests framework `9.0.0` with `rollForward: LatestMinor`, so any installed 9.0.x revision satisfies it. The mod no longer sets a machine-wide `DOTNET_ROOT_X86`; if a private runtime is deployed to `<game>\Accessibility\Runtime\dotnet\x86`, the launcher points at it for the game process only.
