# Foresight and Accessibility Mod Manager

Foresight 0.3.41 beta uses a game-folder native proxy and attach helper. Normal installation, launch and removal require no Windows registry entries. Removing the installed package files disables the mod without leaving a launch redirect.

## Publication

- Repository: https://github.com/buu420/foresight
- Author catalog: https://github.com/buu420/buu-s-mods
- Source URL: https://raw.githubusercontent.com/buu420/buu-s-mods/main/index.json
- Plugin ID `buu420`; game ID `chrono-trigger`; Steam app `613830`.
- Catalog channel `beta`; GitHub release marked prerelease.

Add this author source through Developers â†’ Add source, refresh, and choose the beta. It is a custom author catalog, not the signed central registry. All existing FFVII definitions and releases are retained.

The AMM ZIP contains `manifest.json` and `files/`, built and validated with `amm-author`. The catalog description includes beta limitations and the complete keyboard/controller/battle controls.

## Lifecycle

The pre-install check verifies the supported game and refuses installation while it is running. It protects another mod's proxy. On an older Foresight installation only, it offers the one-time administrator action needed to remove matching old IFEO Debugger/owner values. Foreign values are preserved; unresolved launch redirection blocks installation. This script never registers a debugger or changes global environment variables.

There is no post-install or post-uninstall hook. AMM uses its normal file receipt for updates and uninstall. Fresh installs need no elevated launch-registration step. The standalone package has verification and checksum-based file removal commands.

## Build and validate

```powershell
& '.\tools\Package-Amm.ps1' -PortableRoot '<standalone payload>' -CatalogRoot '<fresh catalog>' -OutputDirectory '<fresh output>'
amm-author index validate --project '<catalog>'
```

Publish the GitHub assets first, then add the beta record with the verified asset URL and SHA-256. Refresh the catalog checkout before pushing to preserve concurrent FFVII releases. Version 0.3.40 remains a historical standalone release and uses its original migration-dependent architecture.
