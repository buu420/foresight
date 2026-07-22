# Ultimate ASI Loader binary provenance

- Upstream: <https://github.com/ThirteenAG/Ultimate-ASI-Loader>
- Upstream installation and usage README reviewed: 2026-07-22
- Embedded source: Reloaded-II 1.30.2 `Loader\Asi\UltimateAsiLoader.7z`, entry `ASILoader32.dll`
- File version: 6.9.0
- Architecture: 32-bit x86
- SHA-256: `A51C630B2EA3D78AD55A330EA64D510C8C0737F620BE65AD7503B61840D59E37`
- Deployment name for Chrono Trigger: `winmm.dll`, selected because the supported game executable imports `WINMM.dll`

The binary is vendored so deployment does not depend on a separately installed archive utility. Reloaded-II's own supported “Deploy ASI Loader” implementation uses the same embedded binary and naming rule.
