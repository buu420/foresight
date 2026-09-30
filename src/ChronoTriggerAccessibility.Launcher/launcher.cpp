// Chrono Trigger Accessibility Launcher (x86)
//
// Registered by the installer as the Windows Image File Execution Options
// "debugger" for Chrono Trigger.exe. When Steam launches the game, Windows runs
// us instead, handing us the game's command line. We then:
//
//   1. Refuse to touch anything that is not the supported Chrono Trigger build
//      (IFEO matches on file name alone, so an unrelated Chrono Trigger.exe can
//      reach us). Unsupported builds are launched plainly, never hooked.
//   2. Borrow %APPDATA%\...\ReloadedII.json via ReloadedPointerLease and aim it
//      at the portable Reloaded-II tree in the game folder.
//   3. CreateProcessW with DEBUG_ONLY_THIS_PROCESS | CREATE_SUSPENDED. The debug
//      flag makes Windows skip the IFEO redirect for the child, so we do not
//      recurse into ourselves; suspension lets us detach cleanly first.
//   4. Detach, resume the primary thread, then inject
//      Reloaded.Mod.Loader.Bootstrapper.dll from the portable tree, resolving
//      LoadLibraryW by remote module base + RVA rather than trusting a local
//      address.
//   5. Wait for exit and forward the exit code. The lease destructor gives the
//      pointer back on every exit path.
//
// The resume-before-inject ordering is deliberate and was arrived at by
// measurement, not preference. See the long comment further down; injecting into
// a pristine suspended process, or while the process is parked at its initial
// loader breakpoint, deadlocks on the loader lock.
//
// Everything is logged next to this exe. Fatal problems also show a MessageBox,
// which is screen-reader accessible.

#include "common.h"

using namespace cta;

constexpr const wchar_t* DIALOG_TITLE      = L"Chrono Trigger Accessibility";
constexpr const wchar_t* LOG_NAME          = L"ChronoTriggerAccessibility.Launcher.log";
constexpr const wchar_t* RECURSION_ENV_VAR = L"CTA_LAUNCHER_ACTIVE";


static void ShowError(const std::wstring& msg) {
    MessageBoxW(nullptr, msg.c_str(), DIALOG_TITLE, MB_OK | MB_ICONERROR);
}

#include "injection.h"

// =========================================================================
// Why the primary thread is resumed BEFORE injecting
//
// It is tempting to hold the process at a known-early point and inject there, so
// no game code can run before the hooks are installed. Two variants were tried
// and both are wrong:
//
//   1. Inject while the process is still CREATE_SUSPENDED and untouched. The
//      loader lock and PEB loader data are not initialised until the primary
//      thread runs LdrInitializeThunk, so remote LoadLibraryW has no initialised
//      loader to work with.
//
//   2. Attach as a debugger, stop at the initial loader breakpoint, suspend the
//      primary thread there, detach, then inject. MEASURED RESULT: hard deadlock.
//      That breakpoint is raised from inside ntdll's process initialisation
//      (LdrpDoDebuggerBreak), which means the primary thread OWNS THE LOADER LOCK
//      at that instant. Suspending it and then calling LoadLibraryW on another
//      thread waits on a lock whose owner can never release it. The launcher sat
//      in WaitForSingleObject until the game was killed.
//
// So the primary thread must be running when we inject. It may briefly hold the
// loader lock during its own initialisation, but because it is running it will
// release it, and our remote thread merely waits instead of deadlocking.
//
// The cost is a short race: the game gets a head start of roughly the time it
// takes us to spawn the remote thread. In practice that is comfortably early --
// the ASI chain this replaced injected considerably later and still announced the
// opening "Square Enix" logo, so there is ample margin before anything the mod
// needs to narrate.
// =========================================================================
// Waiting for exit
// =========================================================================

static int WaitForGameExit(const PROCESS_INFORMATION& pi, Logger& log) {
    DWORD heartbeat = 0;
    for (;;) {
        DWORD w = WaitForSingleObject(pi.hProcess, 30000);
        if (w == WAIT_OBJECT_0) {
            DWORD code = 0; GetExitCodeProcess(pi.hProcess, &code);
            log.W(L"game exited, code=" + std::to_wstring(code));
            return (int)code;
        }
        if (w == WAIT_TIMEOUT) {
            log.W(L"game still running (heartbeat " + std::to_wstring(++heartbeat) + L")");
            continue;
        }
        log.Err(L"WaitForGameExit: WaitForSingleObject", GetLastError());
        return 1;
    }
}

// =========================================================================
// Plain launch, no injection. Used for unsupported builds, a broken payload, or
// a recursion guard trip. DEBUG_ONLY_THIS_PROCESS still bypasses IFEO so we do
// not re-enter ourselves.
// =========================================================================

static int LaunchGamePlain(const fs::path& gameExe, const std::wstring& extraArgs, Logger& log) {
    log.A("LaunchGamePlain: starting the game without the accessibility mod");

    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = {};
    std::wstring cmd = QuoteArgument(gameExe.wstring());
    if (!extraArgs.empty()) cmd += L" " + extraArgs;
    std::vector<wchar_t> cmdBuf(cmd.begin(), cmd.end()); cmdBuf.push_back(0);
    fs::path gameDir = gameExe.parent_path();

    if (!CreateProcessW(gameExe.c_str(), cmdBuf.data(), nullptr, nullptr, FALSE,
                        DEBUG_ONLY_THIS_PROCESS, nullptr, gameDir.c_str(), &si, &pi)) {
        DWORD err = GetLastError();
        log.Err(L"LaunchGamePlain: CreateProcessW", err);
        ShowError(L"Could not start Chrono Trigger.\n\nWindows error " +
                  std::to_wstring(err) + L" (" + Logger::FormatWin32Error(err) + L")\n\n"
                  L"Log: " + log.Path().wstring());
        return 1;
    }

    DebugSetProcessKillOnExit(FALSE);
    if (!DebugActiveProcessStop(pi.dwProcessId))
        log.Err(L"LaunchGamePlain: DebugActiveProcessStop", GetLastError());

    log.W(L"LaunchGamePlain: pid=" + std::to_wstring(pi.dwProcessId));
    int rc = WaitForGameExit(pi, log);
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    return rc;
}

// =========================================================================
// Main flow
// =========================================================================

static int LaunchWithMod(const fs::path& gameExe, const std::wstring& extraArgs, Logger& log) {
    PortableLayout layout(gameExe.parent_path());
    log.W(L"reloadedRoot=" + layout.reloadedRoot.wstring());

    std::wstring missing = layout.FindMissing();
    if (!missing.empty()) {
        log.W(L"required payload files missing:\n" + missing);
        ShowError(L"The Chrono Trigger accessibility mod files are missing, so the game "
                  L"will start without speech.\n\nMissing:\n" + missing +
                  L"\nRe-run the deployment to repair this.\n\nLog: " + log.Path().wstring());
        return LaunchGamePlain(gameExe, extraArgs, log);
    }

    // Borrow the Reloaded pointer. Released on every exit path below.
    ReloadedPointerLease lease(layout.reloadedRoot, log);
    if (!lease.Ok()) {
        ShowError(L"Could not prepare the Reloaded mod loader configuration, so the game "
                  L"will start without speech.\n\n" + lease.Error() +
                  L"\n\nLog: " + log.Path().wstring());
        return LaunchGamePlain(gameExe, extraArgs, log);
    }

    if (!WriteAppConfig(layout.reloadedRoot, gameExe, log)) {
        ShowError(L"Could not write the Reloaded application configuration, so the game "
                  L"will start without speech.\n\nLog: " + log.Path().wstring());
        return LaunchGamePlain(gameExe, extraArgs, log);
    }

    // If a private x86 runtime was deployed beside the game, prefer it. Set only
    // for this process; the child inherits it because lpEnvironment is null.
    std::error_code ec;
    fs::path privateRuntime = layout.gameRoot / L"Accessibility" / L"Runtime" / L"dotnet" / L"x86";
    if (fs::exists(privateRuntime, ec)) {
        SetEnvironmentVariableW(L"DOTNET_ROOT_X86", privateRuntime.wstring().c_str());
        log.W(L"using private x86 runtime at " + privateRuntime.wstring());
    } else {
        log.A("no private x86 runtime deployed; relying on the machine-wide x86 .NET");
    }

    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = {};
    std::wstring cmd = QuoteArgument(gameExe.wstring());
    if (!extraArgs.empty()) cmd += L" " + extraArgs;
    std::vector<wchar_t> cmdBuf(cmd.begin(), cmd.end()); cmdBuf.push_back(0);
    fs::path gameDir = gameExe.parent_path();

    // CREATE_SUSPENDED only so we can detach the debugger before any game code
    // runs. DEBUG_ONLY_THIS_PROCESS is what stops Windows re-applying the IFEO
    // redirect to the child, which would otherwise recurse into this launcher.
    log.W(L"CreateProcessW(DEBUG_ONLY_THIS_PROCESS|CREATE_SUSPENDED) cmd=" + cmd);
    if (!CreateProcessW(gameExe.c_str(), cmdBuf.data(), nullptr, nullptr, FALSE,
                        DEBUG_ONLY_THIS_PROCESS | CREATE_SUSPENDED, nullptr,
                        gameDir.c_str(), &si, &pi)) {
        DWORD err = GetLastError();
        log.Err(L"CreateProcessW", err);
        ShowError(L"Could not start Chrono Trigger.\n\nWindows error " + std::to_wstring(err) +
                  L" (" + Logger::FormatWin32Error(err) + L")\n\nLog: " + log.Path().wstring());
        return 1;
    }
    log.W(L"game pid=" + std::to_wstring(pi.dwProcessId) + L" (suspended)");

    // Detaching must not take the game with it.
    DebugSetProcessKillOnExit(FALSE);
    if (!DebugActiveProcessStop(pi.dwProcessId))
        log.Err(L"DebugActiveProcessStop", GetLastError());
    else
        log.A("detached debug attachment");

    // Must precede injection -- see the long comment above on the loader lock.
    DWORD priorSuspendCount = ResumeThread(pi.hThread);
    if (priorSuspendCount == (DWORD)-1) {
        log.Err(L"ResumeThread", GetLastError());
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
        ShowError(L"Could not start Chrono Trigger's main thread.\n\nLog: " +
                  log.Path().wstring());
        return 1;
    }
    log.W(L"primary thread resumed for loader initialisation (prior suspend count=" +
          std::to_wstring(priorSuspendCount) + L")");

    InjectResult ir = InjectDll(pi.hProcess, pi.dwProcessId,
                                layout.bootstrapperDll.wstring(), log);

    if (ir != InjectResult::Success) {
        log.W(std::wstring(L"injection failed: ") + InjectResultName(ir));
        ShowError(std::wstring(L"Chrono Trigger started, but the accessibility mod could not "
                  L"be loaded, so the game will not speak.\n\nReason: ") +
                  InjectResultName(ir) +
                  L"\n\nClose the game and re-run the deployment to repair this.\n\nLog: " +
                  log.Path().wstring());
    }

    int rc = WaitForGameExit(pi, log);
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    return rc;
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, LPWSTR, int) {
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    int rc = 1;
    Logger log;
    try {
        log.Open(SelfDir(), LOG_NAME);
        log.A("=== Chrono Trigger Accessibility Launcher start ===");
        log.W(L"selfPath=" + SelfPath().wstring());
        log.W(L"commandLine=" + std::wstring(GetCommandLineW()));

        int argc = 0;
        LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
        if (!argv) {
            log.Err(L"CommandLineToArgvW", GetLastError());
            ShowError(L"Could not read the command line. See " + log.Path().wstring());
            throw std::runtime_error("argv parse failed");
        }
        for (int i = 0; i < argc; ++i)
            log.W(L"argv[" + std::to_wstring(i) + L"]=" + std::wstring(argv[i]));

        if (argc < 2) {
            LocalFree(argv);
            ShowError(L"This launcher is started by Windows when you run Chrono Trigger; "
                      L"it is not meant to be run directly.\n\n"
                      L"If Chrono Trigger will not start, run the installer with /uninstall "
                      L"to remove the redirect, then install the mod again.\n\nLog: " +
                      log.Path().wstring());
            throw std::runtime_error("no game path argument");
        }

        fs::path gameExe = argv[1];
        std::wstring extraArgs;
        for (int i = 2; i < argc; ++i) {
            if (!extraArgs.empty()) extraArgs += L" ";
            extraArgs += QuoteArgument(argv[i]);
        }
        LocalFree(argv);
        log.W(L"gameExe=" + gameExe.wstring());

        std::error_code ec;
        if (!fs::exists(gameExe, ec)) {
            log.A("game executable does not exist");
            ShowError(L"Chrono Trigger was not found at:\n\n" + gameExe.wstring() +
                      L"\n\nThe redirect may point at a moved or deleted copy. Run the "
                      L"installer with /uninstall, verify the game in Steam, then install "
                      L"the mod again.\n\nLog: " + log.Path().wstring());
            throw std::runtime_error("game exe missing");
        }

        // Recursion guard. DEBUG_ONLY_THIS_PROCESS should already stop Windows
        // re-applying the IFEO redirect to the child, so this is a backstop only.
        //
        // Presence is the whole signal, so test that and nothing else. An earlier
        // version also required the value to fit a small buffer, which meant an
        // unexpectedly long value silently disabled the guard. GetEnvironmentVariableW
        // returns 0 only when the variable does not exist.
        DWORD envLen = GetEnvironmentVariableW(RECURSION_ENV_VAR, nullptr, 0);
        if (envLen > 0) {
            log.A("recursion guard tripped; launching the game directly");
            rc = LaunchGamePlain(gameExe, extraArgs, log);
        } else {
            SetEnvironmentVariableW(RECURSION_ENV_VAR, L"1");

            // Fail closed on an unknown build: hook nothing rather than hook wrong.
            std::wstring actualHash;
            if (!IsSupportedGameExecutable(gameExe, actualHash)) {
                log.W(L"unsupported executable. expected=" + std::wstring(SUPPORTED_GAME_SHA256) +
                      L" actual=" + (actualHash.empty() ? L"(could not hash)" : actualHash));
                ShowError(L"This Chrono Trigger executable is not the build the accessibility "
                          L"mod supports, so the game will start without speech.\n\n"
                          L"Verify the game files in Steam. The mod deliberately refuses to "
                          L"modify an unknown build.\n\nLog: " + log.Path().wstring());
                rc = LaunchGamePlain(gameExe, extraArgs, log);
            } else {
                log.W(L"supported executable verified: SHA-256 " + actualHash);
                rc = LaunchWithMod(gameExe, extraArgs, log);
            }
        }

        log.W(L"returning rc=" + std::to_wstring(rc));
    } catch (const std::exception& e) {
        log.W(L"std::exception: " + Utf8ToWide(e.what()));
    } catch (...) {
        log.A("unknown exception");
    }

    log.Close();
    CoUninitialize();
    return rc;
}
