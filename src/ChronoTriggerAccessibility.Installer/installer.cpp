// Chrono Trigger Accessibility Installer / Uninstaller (x86)
//
// Writes the Image File Execution Options "Debugger" value that makes Windows
// run our launcher whenever Chrono Trigger.exe starts. That key lives under
// HKLM, so this exe requires elevation and triggers UAC.
//
// Usage:
//   ChronoTriggerAccessibility.Installer.exe            -> install
//   ChronoTriggerAccessibility.Installer.exe /uninstall -> remove
//
// This installer copies no files. Deployment puts the payload in place first;
// this only performs the registry step, after verifying the payload is real.
//
// Ownership marker: alongside "Debugger" we write a private value recording that
// WE created it. Uninstall removes the Debugger value only if that marker is
// present and matches, so we can never delete a redirect belonging to another
// tool that happens to target the same executable name.

#include "../ChronoTriggerAccessibility.Launcher/common.h"
using namespace cta;

constexpr const wchar_t* DIALOG_TITLE   = L"Foresight Beta Installer";
constexpr const wchar_t* LOG_NAME       = L"ChronoTriggerAccessibility.Installer.log";
constexpr const wchar_t* LAUNCHER_NAME  = L"ChronoTriggerAccessibility.Launcher.exe";
constexpr const wchar_t* OWNER_VALUE    = L"ChronoTriggerAccessibilityDebuggerOwner";

constexpr const wchar_t* IFEO_ROOT =
    L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options";

static void ShowError(const std::wstring& m) {
    MessageBoxW(nullptr, m.c_str(), DIALOG_TITLE, MB_OK | MB_ICONERROR);
}
static void ShowInfo(const std::wstring& m) {
    MessageBoxW(nullptr, m.c_str(), DIALOG_TITLE, MB_OK | MB_ICONINFORMATION);
}
static bool AskYesNo(const std::wstring& m) {
    return MessageBoxW(nullptr, m.c_str(), DIALOG_TITLE,
                       MB_YESNO | MB_ICONQUESTION) == IDYES;
}

// =========================================================================
// Elevation
// =========================================================================

static bool IsElevated() {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
    TOKEN_ELEVATION te = {};
    DWORD sz = sizeof(te);
    BOOL ok = GetTokenInformation(token, TokenElevation, &te, sz, &sz);
    CloseHandle(token);
    return ok && te.TokenIsElevated != 0;
}

static bool RelaunchElevated(const std::wstring& args, Logger& log) {
    std::vector<wchar_t> self(MAX_PATH * 4);
    if (!GetModuleFileNameW(nullptr, self.data(), (DWORD)self.size())) {
        log.Err(L"RelaunchElevated: GetModuleFileNameW", GetLastError());
        return false;
    }

    SHELLEXECUTEINFOW sei = {};
    sei.cbSize = sizeof(sei);
    sei.fMask = SEE_MASK_NOCLOSEPROCESS;
    sei.lpVerb = L"runas";
    sei.lpFile = self.data();
    sei.lpParameters = args.empty() ? nullptr : args.c_str();
    sei.nShow = SW_SHOWNORMAL;

    if (!ShellExecuteExW(&sei)) {
        DWORD err = GetLastError();
        log.Err(L"RelaunchElevated: ShellExecuteExW", err);
        if (err == ERROR_CANCELLED) {
            ShowError(L"Administrator access was declined, so nothing was changed.\n\n"
                      L"This installer needs administrator rights to register the mod with "
                      L"Windows. Run it again and choose Yes on the Windows security prompt.");
        } else {
            ShowError(L"Could not restart as administrator.\n\nWindows error " +
                      std::to_wstring(err) + L" (" + Logger::FormatWin32Error(err) + L")\n\n"
                      L"Try right-clicking the installer and choosing "
                      L"\"Run as administrator\".");
        }
        return false;
    }
    if (sei.hProcess) CloseHandle(sei.hProcess);
    log.A("RelaunchElevated: elevated instance started");
    return true;
}

// =========================================================================
// Registry
// =========================================================================

static std::wstring IfeoKeyPath() {
    return std::wstring(IFEO_ROOT) + L"\\" + GAME_EXE_NAME;
}

static bool ReadStringValue(HKEY key, const wchar_t* name, std::wstring& out) {
    DWORD type = 0, bytes = 0;
    if (RegQueryValueExW(key, name, nullptr, &type, nullptr, &bytes) != ERROR_SUCCESS ||
        type != REG_SZ || bytes == 0) {
        return false;
    }
    std::vector<wchar_t> buf(bytes / sizeof(wchar_t) + 1, 0);
    if (RegQueryValueExW(key, name, nullptr, &type,
                         (LPBYTE)buf.data(), &bytes) != ERROR_SUCCESS) {
        return false;
    }
    out.assign(buf.data());
    return true;
}

static LONG RegisterIfeo(const fs::path& launcherExe, Logger& log) {
    std::wstring keyPath = IfeoKeyPath();
    log.W(L"RegisterIfeo: HKLM\\" + keyPath);

    HKEY key = nullptr;
    LONG r = RegCreateKeyExW(HKEY_LOCAL_MACHINE, keyPath.c_str(), 0, nullptr,
                             REG_OPTION_NON_VOLATILE,
                             KEY_SET_VALUE | KEY_QUERY_VALUE | KEY_WOW64_64KEY,
                             nullptr, &key, nullptr);
    if (r != ERROR_SUCCESS) { log.Err(L"RegCreateKeyExW", (DWORD)r); return r; }

    // Refuse to steal somebody else's redirect.
    std::wstring existing, owner;
    if (ReadStringValue(key, L"Debugger", existing) &&
        (!ReadStringValue(key, OWNER_VALUE, owner) || existing != owner)) {
        RegCloseKey(key);
        log.W(L"RegisterIfeo: refusing to replace a foreign Debugger value: " + existing);
        ShowError(L"Another program already redirects Chrono Trigger.exe:\n\n" + existing +
                  L"\n\nThe accessibility mod will not overwrite it. Remove that redirect "
                  L"first, then run this installer again.");
        return ERROR_ALREADY_EXISTS;
    }

    std::wstring value = L"\"" + launcherExe.wstring() + L"\"";
    r = RegSetValueExW(key, L"Debugger", 0, REG_SZ,
                       (const BYTE*)value.c_str(),
                       (DWORD)((value.size() + 1) * sizeof(wchar_t)));
    if (r != ERROR_SUCCESS) {
        log.Err(L"RegSetValueExW Debugger", (DWORD)r);
        RegCloseKey(key);
        return r;
    }

    // Marker proving this redirect is ours, so uninstall is safe.
    LONG m = RegSetValueExW(key, OWNER_VALUE, 0, REG_SZ,
                            (const BYTE*)value.c_str(),
                            (DWORD)((value.size() + 1) * sizeof(wchar_t)));
    if (m != ERROR_SUCCESS) {
        log.Err(L"RegSetValueExW owner marker", (DWORD)m);
        // Registration is incomplete without proof of ownership. Restore the
        // previous redirect (if any), so a failed install cannot trap launches.
        if (existing.empty()) RegDeleteValueW(key, L"Debugger");
        else RegSetValueExW(key, L"Debugger", 0, REG_SZ,
                           reinterpret_cast<const BYTE*>(existing.c_str()),
                           static_cast<DWORD>((existing.size() + 1) * sizeof(wchar_t)));
        RegCloseKey(key);
        return m;
    }

    RegCloseKey(key);
    log.W(L"RegisterIfeo: Debugger = " + value);
    return ERROR_SUCCESS;
}

static LONG UnregisterIfeo(Logger& log) {
    std::wstring keyPath = IfeoKeyPath();
    HKEY key = nullptr;
    LONG r = RegOpenKeyExW(HKEY_LOCAL_MACHINE, keyPath.c_str(), 0,
                           KEY_SET_VALUE | KEY_QUERY_VALUE | KEY_WOW64_64KEY, &key);
    if (r == ERROR_FILE_NOT_FOUND) {
        log.A("UnregisterIfeo: key absent, nothing to remove");
        return ERROR_SUCCESS;
    }
    if (r != ERROR_SUCCESS) { log.Err(L"RegOpenKeyExW", (DWORD)r); return r; }

    std::wstring debuggerValue, owner;
    bool hasDebugger = ReadStringValue(key, L"Debugger", debuggerValue);
    bool hasOwner    = ReadStringValue(key, OWNER_VALUE, owner);

    if (hasDebugger && (!hasOwner || debuggerValue != owner)) {
        RegCloseKey(key);
        log.W(L"UnregisterIfeo: leaving a foreign Debugger value alone: " + debuggerValue);
        ShowError(L"The Chrono Trigger.exe redirect was not created by this mod:\n\n" +
                  debuggerValue + L"\n\nIt has been left untouched. Remove it with whichever "
                  L"tool created it.");
        return ERROR_ACCESS_DENIED;
    }

    if (hasDebugger) {
        r = RegDeleteValueW(key, L"Debugger");
        if (r != ERROR_SUCCESS && r != ERROR_FILE_NOT_FOUND) {
            log.Err(L"RegDeleteValueW Debugger", (DWORD)r);
            RegCloseKey(key);
            return r;
        }
        log.A("UnregisterIfeo: removed Debugger value");
    }
    if (hasOwner) RegDeleteValueW(key, OWNER_VALUE);
    RegCloseKey(key);

    // Keep the key: RegDeleteKeyExW deletes unrelated values too. We own only
    // the matching Debugger value and our marker, never the complete IFEO key.

    return ERROR_SUCCESS;
}

// =========================================================================
// Payload verification
//
// A Debugger value pointing at a missing launcher makes the game unlaunchable,
// so everything is checked before the key is written.
// =========================================================================

struct Target {
    fs::path launcherExe;
    fs::path gameExe;
    fs::path gameRoot;
    std::wstring problem;
};

static Target ResolveTarget(Logger& log) {
    Target t;
    fs::path dir = SelfDir();
    t.launcherExe = dir / LAUNCHER_NAME;

    // Deployed layout is <game>\Accessibility\Launcher\<these exes>, so the game
    // folder is two levels up. Fall back to this folder for a flat layout.
    std::error_code ec;
    fs::path twoUp = dir.parent_path().parent_path();
    if (fs::exists(twoUp / GAME_EXE_NAME, ec))      t.gameRoot = twoUp;
    else if (fs::exists(dir / GAME_EXE_NAME, ec))   t.gameRoot = dir;
    else {
        t.problem = L"Could not find " + std::wstring(GAME_EXE_NAME) +
                    L". Expected it in:\n  " + twoUp.wstring() + L"\n  " + dir.wstring();
        return t;
    }
    t.gameExe = t.gameRoot / GAME_EXE_NAME;

    if (!fs::exists(t.launcherExe, ec)) {
        t.problem = L"The launcher is missing:\n  " + t.launcherExe.wstring();
        return t;
    }

    std::wstring hash;
    if (!IsSupportedGameExecutable(t.gameExe, hash)) {
        t.problem = L"This is not the Chrono Trigger build the mod supports.\n\nExpected "
                    L"SHA-256:\n  " + std::wstring(SUPPORTED_GAME_SHA256) +
                    L"\nFound:\n  " + (hash.empty() ? L"(could not read the file)" : hash) +
                    L"\n\nVerify the game files in Steam.";
        return t;
    }

    PortableLayout layout(t.gameRoot);
    std::wstring missing = layout.FindMissing();
    if (!missing.empty()) {
        t.problem = L"The mod payload is incomplete. Missing:\n" + missing;
        return t;
    }

    log.W(L"ResolveTarget: gameRoot=" + t.gameRoot.wstring());
    log.W(L"ResolveTarget: launcher=" + t.launcherExe.wstring());
    return t;
}

// =========================================================================
// Modes
// =========================================================================

static int RunInstall(Logger& log) {
    log.A("=== install ===");
    Target t = ResolveTarget(log);
    if (!t.problem.empty()) {
        log.W(L"RunInstall: " + t.problem);
        ShowError(t.problem + L"\n\nRun the deployment script first, then this installer.\n\n"
                  L"Log: " + log.Path().wstring());
        return 1;
    }

    if (!AskYesNo(L"Install Foresight, the Chrono Trigger accessibility beta?\n\n"
                  L"This adds a Windows registry entry so the mod loads automatically "
                  L"whenever you start Chrono Trigger, including from Steam.\n\n"
                  L"Game folder:\n" + t.gameRoot.wstring() + L"\n\nProceed?")) {
        log.A("RunInstall: cancelled by the user");
        return ERROR_CANCELLED;
    }

    LONG r = RegisterIfeo(t.launcherExe, log);
    if (r == ERROR_ALREADY_EXISTS) return 1;   // already explained to the user
    if (r != ERROR_SUCCESS) {
        ShowError(L"Could not write the Windows registry entry.\n\nError " +
                  std::to_wstring(r) + L" (" + Logger::FormatWin32Error((DWORD)r) + L")\n\n"
                  L"If you did not see a Windows security prompt, right-click the installer "
                  L"and choose \"Run as administrator\".\n\nLog: " + log.Path().wstring());
        return 1;
    }

    ShowInfo(L"Foresight beta is installed.\n\nStart Chrono Trigger from Steam as "
             L"usual and it will speak. Start your screen reader first.\n\n"
             L"To remove it, run this installer again with /uninstall.");
    log.A("RunInstall: complete");
    return 0;
}

static int RunUninstall(Logger& log) {
    log.A("=== uninstall ===");
    if (!AskYesNo(L"Remove Foresight's registry entry?\n\n"
                  L"The game will then start without speech. Mod files are not deleted. "
                  L"After success, use Foresight-SHA256SUMS.txt to identify this mod's files. "
                  L"Preserve shared mod files, backups and separate audio-description packs.")) {
        log.A("RunUninstall: cancelled by the user");
        return ERROR_CANCELLED;
    }

    LONG r = UnregisterIfeo(log);
    if (r == ERROR_ACCESS_DENIED) return 1;   // already explained to the user
    if (r != ERROR_SUCCESS) {
        ShowError(L"Could not remove the registry entry.\n\nError " + std::to_wstring(r) +
                  L" (" + Logger::FormatWin32Error((DWORD)r) + L")\n\n"
                  L"Try running as administrator.\n\nLog: " + log.Path().wstring());
        return 1;
    }

    ShowInfo(L"Foresight is uninstalled and Chrono Trigger will now start normally.\n\n"
             L"Mod files are left in place. Foresight-SHA256SUMS.txt lists this release's files. "
             L"Preserve shared mod files, backups and separate audio-description packs. "
             L"See Foresight-README.md for removal instructions.");
    log.A("RunUninstall: complete");
    return 0;
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, LPWSTR, int) {
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    int rc = 1;
    Logger log;
    try {
        log.Open(SelfDir(), LOG_NAME);
        log.A("=== Chrono Trigger Accessibility Installer start ===");
        log.W(L"selfPath=" + SelfPath().wstring());
        log.W(L"commandLine=" + std::wstring(GetCommandLineW()));

        int argc = 0;
        LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
        bool uninstall = false;
        if (argv) {
            for (int i = 1; i < argc; ++i) {
                std::wstring a = ToLower(argv[i]);
                if (a == L"/uninstall" || a == L"-uninstall" || a == L"--uninstall")
                    uninstall = true;
            }
            LocalFree(argv);
        }
        log.W(std::wstring(L"mode=") + (uninstall ? L"uninstall" : L"install"));

        if (!IsElevated()) {
            log.A("not elevated; requesting elevation");
            rc = RelaunchElevated(uninstall ? L"/uninstall" : L"", log) ? 0 : 1;
        } else {
            rc = uninstall ? RunUninstall(log) : RunInstall(log);
        }
        log.W(L"rc=" + std::to_wstring(rc));
    } catch (const std::exception& e) {
        log.W(L"std::exception: " + Utf8ToWide(e.what()));
    } catch (...) {
        log.A("unknown exception");
    }

    log.Close();
    CoUninitialize();
    return rc;
}
