# Third-Party Notices

Chrono Trigger Accessibility uses the following third-party components. Chrono Trigger and its assets are not included in the mod package.

## Prism 0.17.3

Prism, the Platform-agnostic Reader Interface for Speech and Messages, is distributed under the Mozilla Public License 2.0. The package contains the upstream `LICENSE` and `NOTICE` files alongside the reviewed 32-bit Windows binary.

- Upstream: <https://github.com/ethindp/prism>
- Pinned tag: `v0.17.3`
- Pinned source commit: `9911156998b52fee91fb2cb4f71ac793d4e546c7`
- Packaged `prism.dll` SHA-256: `6A84322E42D1B4123E2E66E9887CFDF0CDEA2A972FA40FC7B7185AEC77F5178A`

Prism's upstream `NOTICE` identifies bundled or incorporated work including simdutf, NVDA controller RPC definitions, Moderncom, dr_wav, Djinni, concurrentqueue, and fmt. The notice and exact upstream `LICENSES` subtree from the pinned source commit are reproduced unmodified in the package, including each bundled component's license and the Moderncom authors file.

## Reloaded-II interfaces and hooks

The mod is loaded by an existing Reloaded-II installation; Reloaded-II itself is not redistributed in this package.

- Reloaded-II 1.30.2: GNU General Public License version 3; <https://github.com/Reloaded-Project/Reloaded-II>
- Reloaded.Mod.Interfaces 2.5.0: GNU General Public License version 3. This dependency is used as a loader-provided interface and its assembly is not copied into the mod package.
- Reloaded.Hooks.Definitions 1.15.0: GNU Lesser General Public License version 3; <https://github.com/Reloaded-Project/Reloaded.Hooks>
- Reloaded.SharedLib.Hooks 1.9.0 compile-time interfaces: GNU Lesser General Public License version 3. The package includes `Reloaded.Hooks.ReloadedII.Interfaces.dll`; the working hook implementation is supplied by the separately installed `reloaded.sharedlib.hooks` Reloaded mod.

The package's `LICENSES` directory includes the GPL 3.0 text and the exact LGPL 3.0 license files shipped by the pinned Reloaded NuGet packages. Corresponding sources remain available from the upstream repositories and NuGet packages. No Reloaded binary has been modified by this project.

## Microsoft .NET

The 32-bit Microsoft .NET 9.0.18 runtime and Windows Desktop runtime are external prerequisites and are not included in the mod package. Their licensing remains governed by Microsoft. The accessible launcher points Reloaded's x86 process to the existing per-user runtime without changing machine-wide runtime configuration.
