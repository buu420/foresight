# Reloaded-II 1.30.3 — vendored x86 loader payload

These are **unmodified upstream binaries**. Nothing here was patched, recompiled,
or converted between architectures.

| | |
|---|---|
| Project | [Reloaded-II](https://github.com/Reloaded-Project/Reloaded-II) by Sewer56 |
| Version | 1.30.3 (`Reloaded.Mod.Loader.dll` file version 1.30.3.0) |
| Retrieved | 2026-08-18 |
| Copied from | the `Reloaded-II\Loader\X86` tree of an existing local Reloaded-II 1.30.3 installation |
| Licence | GPL-3.0 — see `LICENSES/GNU-GPL-3.0.txt` in the packaged mod |

`mods/reloaded.sharedlib.hooks` is Reloaded II Shared Lib: Reloaded.Hooks
**1.16.3**, LGPL-3.0. It is a *universal* mod and legitimately ships both `x86`
and `x64` payloads; the x64 files are expected and must not be stripped.

## Why the loader must stay pure x86

`Chrono Trigger.exe` is a 32-bit (`IMAGE_FILE_MACHINE_I386`, `0x14C`) process.
The bootstrapper is native x86 and the managed assemblies are ReadyToRun with
x86 code baked in, so they are architecture-locked — an x64 payload cannot be
converted or substituted. `tools\Build-Native.ps1` and
`tools\Verify-Deployment.ps1` both assert `0x14C` on every file below `Loader\`.

## Layout

This tree mirrors the deployed layout exactly, so deployment is a straight copy:

```
Loader\X86\**                        ->  <game>\Reloaded-II\Loader\X86\**
mods\reloaded.sharedlib.hooks\**      ->  <game>\Reloaded-II\Mods\reloaded.sharedlib.hooks\**
```

## SHA-256

### Loader\X86

| SHA-256 | Size | Path |
|---|---|---|
| `997A8EC95434239AFEFF0802849043EC49ED51459394D5DC97375D1914606329` | 132096 | `Loader/X86/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll` |
| `5243AE823DFFEC7AA2597B605A8336C37CEB8C137107EB24DDC8C2DF934E118E` | 36864 | `Loader/X86/Colorful.Console.dll` |
| `2296E04035CE9631C38E23301452E10A480DA6A4E61F0921970CE94A3AA221E7` | 27246 | `Loader/X86/DelayInjectHooks.json` |
| `632B630F6B05D2A05D485A2B16FFD28861B1915B4B2E95AE3E69AD31EF7A65AA` | 24576 | `Loader/X86/Indieteur.SAMAPI.dll` |
| `CD5CB4C5A8000567314E3C5DA409D50382AAB23125D9E250B19DB354339432BB` | 32768 | `Loader/X86/Indieteur.VDFAPI.dll` |
| `8DCB5597BB0F5FE5E513E7809A7F9826E1CCDA0BBA57EBE581B27968AB2262E5` | 65536 | `Loader/X86/McMaster.NETCore.Plugins.dll` |
| `D0F5420B19DA2C278530665E16BE1671E0810B4EFE6B6A93E68A36167A175508` | 28672 | `Loader/X86/Reloaded.Memory.dll` |
| `F7BCC0BC86AF845FD34795262E842E9EC122B4D7BDC46B906C162BC7DD7D7B3E` | 40960 | `Loader/X86/Reloaded.Mod.Interfaces.dll` |
| `9A7C477B36CFB35F163C82E5E66D04D1B0A7BB7A4DCC32C3D80A0E12D7DF9409` | 3983 | `Loader/X86/Reloaded.Mod.Loader.deps.json` |
| `B6EC17726270ACF0DDFA707162D172B60820DD312FAB1758B6110389D6DED30F` | 163840 | `Loader/X86/Reloaded.Mod.Loader.dll` |
| `8AA0D046DE38E743EA5E5547B1D571B734561C503C709085A53ABBD711354F63` | 339968 | `Loader/X86/Reloaded.Mod.Loader.IO.dll` |
| `2CD7DD1EBB7A203244AFEF13B91A64780EC0BAEDA3086A6EA3FAD5073531AA3F` | 521 | `Loader/X86/Reloaded.Mod.Loader.runtimeconfig.json` |

### mods\reloaded.sharedlib.hooks

| SHA-256 | Size | Path |
|---|---|---|
| `ED086FA604A40472596638F549A541ED02B254F4B62AFB33E88C371DEE719CFD` | 1395 | `mods/reloaded.sharedlib.hooks/ModConfig.json` |
| `C32E9ADD5C5B0179548EE6FF5BB1EC74431BB7F818DDD3531296974DE891539B` | 31424 | `mods/reloaded.sharedlib.hooks/Preview.png` |
| `AE9D6AC14B58B59F3BE8D27F96542BB60D6DB9F0D6F63E411CF17A2B93DCDFB5` | 2666 | `mods/reloaded.sharedlib.hooks/Sewer56.Update.Metadata.json` |
| `9732B336AA494898EDA6E1F20740ABD0236BA905653139B0F954B9776740261B` | 1820 | `mods/reloaded.sharedlib.hooks/x64/FASM-LICENSE.TXT` |
| `8EDD78F35DFF3A3009A7D8E3E4F753880A694512E5C301101B0A3A838271DF0E` | 112640 | `mods/reloaded.sharedlib.hooks/x64/FASM.DLL` |
| `2E3BA7479E764206008E1DCD7754ECA23E4F863EBEF3F15D3E1DF58BBEE55497` | 116736 | `mods/reloaded.sharedlib.hooks/x64/FASMX64.DLL` |
| `D383921F7A3197BEA9E7C7A8A740DF5F0A6DF10E73BECD7F3B6E6120FE0DF11F` | 1159168 | `mods/reloaded.sharedlib.hooks/x64/Iced.dll` |
| `AF966E00321217387B67ECEEE802FF0E61430031DD07238E31FB1D1E16FB4E7C` | 28672 | `mods/reloaded.sharedlib.hooks/x64/Reloaded.Assembler.dll` |
| `CDAF3554A608420878D896FDA73C3AF5967BA6BBDC9AFE9E92F4E2A8C2553E0C` | 393 | `mods/reloaded.sharedlib.hooks/x64/Reloaded.Assembler.targets` |
| `5122E69DDACF1F17E7CF2DC2B8D88080895AA9F3C2E375D794CBAE5B447F1CFA` | 114688 | `mods/reloaded.sharedlib.hooks/x64/Reloaded.Hooks.Definitions.dll` |
| `185702CFA263BF0F49C118ECC834EFADDAA150602FF2DBFDC51A776FFA3E1E99` | 126976 | `mods/reloaded.sharedlib.hooks/x64/Reloaded.Hooks.dll` |
| `1266DEDEDA69757B97FF5E3BB29470C73598F257756841B9FEF4BF92617BE9B3` | 5750 | `mods/reloaded.sharedlib.hooks/x64/Reloaded.Hooks.ReloadedII.deps.json` |
| `EC4731AE72334F836E12B4C4DA389AD44831E966C4D28FC2218FD7ED01499E9E` | 20480 | `mods/reloaded.sharedlib.hooks/x64/Reloaded.Hooks.ReloadedII.dll` |
| `0684D7165B3332EAE7B14CF408A322E6D2DBA2CB288F02E7510703081F628843` | 20480 | `mods/reloaded.sharedlib.hooks/x64/Reloaded.Hooks.ReloadedII.Interfaces.dll` |
| `489A92C37422B7E5CE212F9701B7A081D887B895F88B91B000976131F90F823F` | 253 | `mods/reloaded.sharedlib.hooks/x64/Reloaded.Hooks.ReloadedII.runtimeconfig.json` |
| `4AB9809730322567AD2D61238F168EB1A134E6A5BDAB52684306A0A13BC3181B` | 53248 | `mods/reloaded.sharedlib.hooks/x64/Reloaded.Memory.Buffers.dll` |
| `45CBB591BBB17A6E796C857095CA0283D1A0C47D42BE69AD76216DF130A345A6` | 28672 | `mods/reloaded.sharedlib.hooks/x64/Reloaded.Memory.dll` |
| `811A1EA371F67F72BBC9C6331314848F849314E7182A2CC16F0951902A1BFB0C` | 40960 | `mods/reloaded.sharedlib.hooks/x64/Reloaded.Mod.Interfaces.dll` |
| `9732B336AA494898EDA6E1F20740ABD0236BA905653139B0F954B9776740261B` | 1820 | `mods/reloaded.sharedlib.hooks/x86/FASM-LICENSE.TXT` |
| `8EDD78F35DFF3A3009A7D8E3E4F753880A694512E5C301101B0A3A838271DF0E` | 112640 | `mods/reloaded.sharedlib.hooks/x86/FASM.DLL` |
| `2E3BA7479E764206008E1DCD7754ECA23E4F863EBEF3F15D3E1DF58BBEE55497` | 116736 | `mods/reloaded.sharedlib.hooks/x86/FASMX64.DLL` |
| `7BD4D8140E7C4C6892736A4B5CFD00AD30CBD7B3691C6A324B0087914A8268EB` | 1134592 | `mods/reloaded.sharedlib.hooks/x86/Iced.dll` |
| `75060C30F045763964ABEC032436D1949128AC2F12CE9FD493480CBA86E7A61B` | 28672 | `mods/reloaded.sharedlib.hooks/x86/Reloaded.Assembler.dll` |
| `CDAF3554A608420878D896FDA73C3AF5967BA6BBDC9AFE9E92F4E2A8C2553E0C` | 393 | `mods/reloaded.sharedlib.hooks/x86/Reloaded.Assembler.targets` |
| `FC92FB0C241BAEDE46E7882EDBE60F0F960F2366DA650FABA72C5AFE6D4CEBB1` | 110592 | `mods/reloaded.sharedlib.hooks/x86/Reloaded.Hooks.Definitions.dll` |
| `FD5A07FD431FB56541D80D293F5852254768025D39B1749994FE5519EE7C5B93` | 114688 | `mods/reloaded.sharedlib.hooks/x86/Reloaded.Hooks.dll` |
| `9A676A2572A948FD222402B3A20D10BB41EECC2D92CD3AC71A42EFA1182DF717` | 5750 | `mods/reloaded.sharedlib.hooks/x86/Reloaded.Hooks.ReloadedII.deps.json` |
| `C315D96496BDC7DF25BF7959CC04E48B780476ABEE71CE9E5479016EFE5B4B84` | 20480 | `mods/reloaded.sharedlib.hooks/x86/Reloaded.Hooks.ReloadedII.dll` |
| `2DE8B5724A4A281454A40E1A4545253CF5FD34E8CDB05CFA747815DA313146E3` | 20480 | `mods/reloaded.sharedlib.hooks/x86/Reloaded.Hooks.ReloadedII.Interfaces.dll` |
| `489A92C37422B7E5CE212F9701B7A081D887B895F88B91B000976131F90F823F` | 253 | `mods/reloaded.sharedlib.hooks/x86/Reloaded.Hooks.ReloadedII.runtimeconfig.json` |
| `C96BD3C340A17760E350A33587807D19C1F1CA1E1307F7BBE7E58746C928B763` | 53248 | `mods/reloaded.sharedlib.hooks/x86/Reloaded.Memory.Buffers.dll` |
| `AE0C62AD9A68F91A2D1181549E9B3F088D7F1C4276BB7B2D044AFB4F0F9602B3` | 28672 | `mods/reloaded.sharedlib.hooks/x86/Reloaded.Memory.dll` |
| `9432378F84DE51ECB19DA7F4D69300244C5B81059D6ED19AC414C8B8F8856A9A` | 40960 | `mods/reloaded.sharedlib.hooks/x86/Reloaded.Mod.Interfaces.dll` |
