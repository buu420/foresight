// Shared helpers for ChronoTriggerAccessibility.Launcher and
// ChronoTriggerAccessibility.Installer. Header-only so both vcxproj projects can
// include it without a separate .lib.
//
// Ported from the user's DSTS mod (DSTS.Common/common.h) with the Reloaded
// pointer handling replaced by the crash-safe lease protocol from the user's
// Blind Soldier mod (native/BlindSoldier.Bootstrap/reloaded_session.cpp).
#pragma once

#define WIN32_LEAN_AND_MEAN
#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <bcrypt.h>
#include <shlobj.h>
#include <shellapi.h>
#include <tlhelp32.h>
#include <string>
#include <vector>
#include <filesystem>
#include <fstream>
#include <cstdio>
#include <cwctype>
#include <stdexcept>

#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "user32.lib")

namespace cta {
namespace fs = std::filesystem;

// The one Chrono Trigger build this mod supports. The launcher refuses to inject
// into anything else rather than hooking unknown bytes.
constexpr const wchar_t* SUPPORTED_GAME_SHA256 =
    L"8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7";

constexpr const wchar_t* GAME_EXE_NAME = L"Chrono Trigger.exe";
constexpr const wchar_t* MOD_ID        = L"chrono.trigger.accessibility";
constexpr const wchar_t* MOD_DLL_NAME  = L"ChronoTriggerAccessibility.Mod.dll";
constexpr const wchar_t* HOOKS_MOD_ID  = L"reloaded.sharedlib.hooks";

// =========================================================================
// Logger. Binary UTF-8 file, flushed on every line.
// =========================================================================

class Logger {
public:
    void Open(const fs::path& dir, const wchar_t* filename) {
        if (m_file) return;
        try {
            std::error_code ec;
            fs::create_directories(dir, ec);
            fs::path p = dir / filename;
            _wfopen_s(&m_file, p.c_str(), L"wb");
            if (m_file) {
                unsigned char bom[3] = { 0xEF, 0xBB, 0xBF };
                fwrite(bom, 1, 3, m_file);
            }
            m_path = p;
        } catch (...) { m_file = nullptr; }
    }
    void Close() { if (m_file) { fclose(m_file); m_file = nullptr; } }
    const fs::path& Path() const { return m_path; }

    void W(const std::wstring& msg) {
        if (!m_file) return;
        WritePrefix();
        WriteUtf8(msg);
        fwrite("\r\n", 1, 2, m_file);
        fflush(m_file);
    }

    void A(const char* msg) {
        if (!m_file || !msg) return;
        WritePrefix();
        fwrite(msg, 1, strlen(msg), m_file);
        fwrite("\r\n", 1, 2, m_file);
        fflush(m_file);
    }

    void Err(const std::wstring& where, DWORD err) {
        W(where + L": err=" + std::to_wstring(err) + L" (" + FormatWin32Error(err) + L")");
    }

    static std::wstring FormatWin32Error(DWORD err) {
        if (err == 0) return L"(no error)";
        LPWSTR buf = nullptr;
        DWORD n = FormatMessageW(
            FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM
            | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr, err, 0, (LPWSTR)&buf, 0, nullptr);
        std::wstring r;
        if (n && buf) {
            r.assign(buf, n);
            while (!r.empty() && (r.back() == L'\n' || r.back() == L'\r' || r.back() == L' '))
                r.pop_back();
            LocalFree(buf);
        }
        if (r.empty()) r = L"(no message)";
        return r;
    }

private:
    FILE* m_file = nullptr;
    fs::path m_path;

    void WritePrefix() {
        SYSTEMTIME st; GetLocalTime(&st);
        char prefix[64];
        int n = _snprintf_s(prefix, sizeof(prefix), _TRUNCATE,
            "[%04u-%02u-%02u %02u:%02u:%02u.%03u] ",
            st.wYear, st.wMonth, st.wDay,
            st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
        if (n > 0) fwrite(prefix, 1, (size_t)n, m_file);
    }

    void WriteUtf8(const std::wstring& w) {
        if (w.empty()) return;
        int len = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), (int)w.size(),
                                      nullptr, 0, nullptr, nullptr);
        if (len <= 0) return;
        std::vector<char> buf((size_t)len);
        WideCharToMultiByte(CP_UTF8, 0, w.c_str(), (int)w.size(),
                            buf.data(), len, nullptr, nullptr);
        fwrite(buf.data(), 1, (size_t)len, m_file);
    }
};

// =========================================================================
// String and path helpers
// =========================================================================

inline std::wstring ToLower(std::wstring s) {
    for (auto& c : s) c = (wchar_t)towlower(c);
    return s;
}

inline std::wstring Utf8ToWide(const std::string& s) {
    if (s.empty()) return {};
    int len = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), nullptr, 0);
    std::wstring r((size_t)len, 0);
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), r.data(), len);
    return r;
}

inline std::string WideToUtf8(const std::wstring& s) {
    if (s.empty()) return {};
    int len = WideCharToMultiByte(CP_UTF8, 0, s.c_str(), (int)s.size(),
                                  nullptr, 0, nullptr, nullptr);
    std::string r((size_t)len, 0);
    WideCharToMultiByte(CP_UTF8, 0, s.c_str(), (int)s.size(),
                        r.data(), len, nullptr, nullptr);
    return r;
}

inline std::wstring JsonEscape(const std::wstring& s) {
    std::wstring r; r.reserve(s.size());
    for (wchar_t c : s) {
        switch (c) {
            case L'\\': r += L"\\\\"; break;
            case L'"':  r += L"\\\""; break;
            case L'\n': r += L"\\n"; break;
            case L'\r': r += L"\\r"; break;
            case L'\t': r += L"\\t"; break;
            default:    r += c;
        }
    }
    return r;
}

inline fs::path SelfPath() {
    std::vector<wchar_t> buf(MAX_PATH * 4);
    DWORD n = GetModuleFileNameW(nullptr, buf.data(), (DWORD)buf.size());
    return fs::path(buf.data(), buf.data() + n);
}
inline fs::path SelfDir() { return SelfPath().parent_path(); }

inline fs::path AppDataRoaming() {
    PWSTR p = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_RoamingAppData, 0, nullptr, &p)))
        return {};
    fs::path r(p); CoTaskMemFree(p); return r;
}

// Reloaded hardcodes this location in BOTH the managed loader
// (Reloaded.Mod.Loader.IO/Paths.cs) and the C++ bootstrapper, which is why a
// launcher-less portable tree has to borrow it for the duration of a launch.
inline fs::path ReloadedIIPointerFile() {
    auto a = AppDataRoaming();
    if (a.empty()) return {};
    return a / L"Reloaded-Mod-Loader-II" / L"ReloadedII.json";
}

inline bool WriteUtf8File(const fs::path& path, const std::wstring& content) {
    std::error_code ec;
    fs::create_directories(path.parent_path(), ec);
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    if (!out) return false;
    std::string utf8 = WideToUtf8(content);
    out.write(utf8.data(), (std::streamsize)utf8.size());
    out.flush();
    return out.good();
}

inline bool ReadUtf8File(const fs::path& path, std::string& out) {
    std::ifstream in(path, std::ios::binary);
    if (!in) return false;
    out.assign(std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>());
    return true;
}

// =========================================================================
// SHA-256 of a file, uppercase hex. Used to fail closed on an unknown build.
// =========================================================================

inline bool FileSha256(const fs::path& path, std::wstring& outHex) {
    outHex.clear();
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                              OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;

    BCRYPT_ALG_HANDLE alg = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    bool ok = false;
    std::vector<UCHAR> digest(32);
    std::vector<UCHAR> buffer(64 * 1024);

    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0 &&
        BCryptCreateHash(alg, &hash, nullptr, 0, nullptr, 0, 0) == 0) {
        ok = true;
        DWORD read = 0;
        while (ReadFile(file, buffer.data(), (DWORD)buffer.size(), &read, nullptr) && read > 0) {
            if (BCryptHashData(hash, buffer.data(), read, 0) != 0) { ok = false; break; }
        }
        if (ok && BCryptFinishHash(hash, digest.data(), (ULONG)digest.size(), 0) == 0) {
            wchar_t hex[65] = {};
            for (int i = 0; i < 32; ++i)
                swprintf_s(hex + i * 2, 3, L"%02X", digest[(size_t)i]);
            outHex.assign(hex, 64);
        } else {
            ok = false;
        }
    }

    if (hash) BCryptDestroyHash(hash);
    if (alg) BCryptCloseAlgorithmProvider(alg, 0);
    CloseHandle(file);
    return ok && outHex.size() == 64;
}

inline bool IsSupportedGameExecutable(const fs::path& gameExe, std::wstring& actualHash) {
    if (!FileSha256(gameExe, actualHash)) return false;
    return _wcsicmp(actualHash.c_str(), SUPPORTED_GAME_SHA256) == 0;
}

// =========================================================================
// Portable tree layout. Everything derives from the game folder; no literals.
// =========================================================================

struct PortableLayout {
    fs::path gameRoot;
    fs::path reloadedRoot;
    fs::path loaderDll;
    fs::path bootstrapperDll;
    fs::path modDll;
    fs::path hooksModConfig;

    explicit PortableLayout(const fs::path& gameFolder) {
        gameRoot        = gameFolder;
        reloadedRoot    = gameRoot / L"Reloaded-II";
        fs::path x86    = reloadedRoot / L"Loader" / L"X86";
        loaderDll       = x86 / L"Reloaded.Mod.Loader.dll";
        bootstrapperDll = x86 / L"Bootstrapper" / L"Reloaded.Mod.Loader.Bootstrapper.dll";
        modDll          = reloadedRoot / L"Mods" / MOD_ID / MOD_DLL_NAME;
        hooksModConfig  = reloadedRoot / L"Mods" / HOOKS_MOD_ID / L"ModConfig.json";
    }

    // Returns a newline-separated list of missing required payload files, empty if complete.
    std::wstring FindMissing() const {
        std::error_code ec;
        std::wstring missing;
        for (const fs::path* p : { &loaderDll, &bootstrapperDll, &modDll, &hooksModConfig }) {
            if (!fs::exists(*p, ec)) missing += L"  - " + p->wstring() + L"\n";
        }
        return missing;
    }
};

// =========================================================================
// Reloaded config writers
// =========================================================================

// The pointer content aimed at our portable tree. LauncherPath is deliberately
// empty: with no launcher exe, Reloaded's GetLauncherFolder() then falls back to
// the loader's own folder, where no portable.txt exists, so the explicit
// directories below are the ones that take effect.
inline std::wstring ReloadedIIPointerContent(const fs::path& reloadedRoot) {
    auto j = [](const fs::path& p) { return JsonEscape(p.wstring()); };
    return
        L"{\n"
        L"  \"LoaderPath32\": \"" + j(reloadedRoot / L"Loader" / L"X86" / L"Reloaded.Mod.Loader.dll") + L"\",\n"
        L"  \"LoaderPath64\": \"" + j(reloadedRoot / L"Loader" / L"X64" / L"Reloaded.Mod.Loader.dll") + L"\",\n"
        L"  \"Bootstrapper32Path\": \"" + j(reloadedRoot / L"Loader" / L"X86" / L"Bootstrapper" / L"Reloaded.Mod.Loader.Bootstrapper.dll") + L"\",\n"
        L"  \"Bootstrapper64Path\": \"" + j(reloadedRoot / L"Loader" / L"X64" / L"Bootstrapper" / L"Reloaded.Mod.Loader.Bootstrapper.dll") + L"\",\n"
        L"  \"LauncherPath\": \"\",\n"
        L"  \"ApplicationConfigDirectory\": \"" + j(reloadedRoot / L"Apps") + L"\",\n"
        L"  \"ModConfigDirectory\": \"" + j(reloadedRoot / L"Mods") + L"\",\n"
        L"  \"ModUserConfigDirectory\": \"" + j(reloadedRoot / L"User" / L"Mods") + L"\",\n"
        L"  \"MiscConfigDirectory\": \"" + j(reloadedRoot / L"User" / L"Misc") + L"\",\n"
        L"  \"PluginConfigDirectory\": \"" + j(reloadedRoot / L"Plugins") + L"\",\n"
        L"  \"EnabledPlugins\": [],\n"
        L"  \"ShowConsole\": false\n"
        L"}\n";
}

// reloaded.sharedlib.hooks must be listed explicitly. The mod declares it as a
// dependency, but with no shared install and no launcher GUI to resolve
// dependencies there is nothing else to enable it.
inline bool WriteAppConfig(const fs::path& reloadedRoot,
                           const fs::path& gameExe,
                           Logger& log) {
    std::wstring exeName = gameExe.filename().wstring();
    fs::path appDir = reloadedRoot / L"Apps" / ToLower(exeName);
    std::wstring content =
        L"{\n"
        L"  \"AppId\": \"" + JsonEscape(ToLower(exeName)) + L"\",\n"
        L"  \"AppName\": \"Chrono Trigger\",\n"
        L"  \"AppLocation\": \"" + JsonEscape(gameExe.wstring()) + L"\",\n"
        L"  \"AppArguments\": \"\",\n"
        L"  \"AppIcon\": \"\",\n"
        L"  \"AutoInject\": false,\n"
        L"  \"EnabledMods\": [\n"
        L"    \"" + std::wstring(HOOKS_MOD_ID) + L"\",\n"
        L"    \"" + std::wstring(MOD_ID) + L"\"\n"
        L"  ],\n"
        L"  \"WorkingDirectory\": \"" + JsonEscape(gameExe.parent_path().wstring()) + L"\",\n"
        L"  \"PluginData\": {},\n"
        L"  \"SortedMods\": [\n"
        L"    \"" + std::wstring(HOOKS_MOD_ID) + L"\",\n"
        L"    \"" + std::wstring(MOD_ID) + L"\"\n"
        L"  ],\n"
        L"  \"PreserveDisabledModOrder\": true,\n"
        L"  \"DontInject\": false,\n"
        L"  \"IsMsStore\": false\n"
        L"}\n";
    fs::path path = appDir / L"AppConfig.json";
    bool ok = WriteUtf8File(path, content);
    log.W(std::wstring(L"WriteAppConfig: ") + (ok ? L"OK" : L"FAILED") + L" path=" + path.wstring());
    return ok;
}

// =========================================================================
// ReloadedPointerLease
//
// Borrows %APPDATA%\Reloaded-Mod-Loader-II\ReloadedII.json for the duration of a
// launch and gives it back. Ported from Blind Soldier's ReloadedPointerLease so
// Chrono Trigger and Final Fantasy VII cannot corrupt each other's pointer.
//
// Deliberately keeps Blind Soldier's mutex NAME. The mutex protects a shared
// global file, so matching the name is what makes the exclusion real; a
// differently-named mutex would let both mods swap the pointer concurrently.
//
// Crash safety: the backup is a durable file, not in-memory state. If this
// process is killed before the destructor runs, the next launch finds the
// leftover backup and restores it before doing anything else.
// =========================================================================

class ReloadedPointerLease {
public:
    ReloadedPointerLease(const fs::path& reloadedRoot, Logger& log,
                         DWORD waitMilliseconds = 30000)
        : m_log(&log) {
        m_pointer = ReloadedIIPointerFile();
        if (m_pointer.empty()) { Fail(L"Reloaded pointer path is unavailable."); return; }

        std::wstring mutexName = L"Local\\BlindSoldier.ReloadedPointer." +
                                 std::to_wstring(HashPath(m_pointer.wstring()));
        m_mutex = CreateMutexW(nullptr, FALSE, mutexName.c_str());
        if (!m_mutex) {
            Fail(L"Could not create the Reloaded pointer mutex: " +
                 Logger::FormatWin32Error(GetLastError()));
            return;
        }
        DWORD wait = WaitForSingleObject(m_mutex, waitMilliseconds);
        if (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED) {
            Fail(wait == WAIT_TIMEOUT
                ? L"Timed out waiting for another Reloaded session to release the pointer."
                : L"Could not acquire the Reloaded pointer mutex: " +
                  Logger::FormatWin32Error(GetLastError()));
            return;
        }
        m_ownsMutex = true;
        if (wait == WAIT_ABANDONED) m_log->A("ReloadedPointerLease: recovering abandoned mutex");

        m_ourContent = ReloadedIIPointerContent(reloadedRoot);
        std::error_code ec;
        fs::create_directories(m_pointer.parent_path(), ec);
        if (ec) {
            Fail(L"Could not create the Reloaded pointer directory: " + Utf8ToWide(ec.message()));
            return;
        }

        m_backup = m_pointer;
        m_backup += L".chrono_trigger_backup";

        if (!RecoverLeftoverBackup()) return;

        // Move the current pointer aside durably, then install ours.
        if (fs::exists(m_pointer, ec)) {
            if (!MoveFileExW(m_pointer.c_str(), m_backup.c_str(),
                             MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
                Fail(L"Could not create the Reloaded pointer backup: " +
                     Logger::FormatWin32Error(GetLastError()));
                return;
            }
            m_hadOriginal = true;
            m_log->A("ReloadedPointerLease: original pointer backed up");
        } else {
            m_log->A("ReloadedPointerLease: no existing pointer to back up");
        }

        if (!WriteUtf8File(m_pointer, m_ourContent)) {
            m_log->A("ReloadedPointerLease: writing our pointer failed; restoring");
            RestoreFromBackup();
            Fail(L"Could not write the portable Reloaded pointer.");
            return;
        }
        m_wroteOurs = true;
        m_log->W(L"ReloadedPointerLease: pointer aimed at " + reloadedRoot.wstring());
    }

    ~ReloadedPointerLease() {
        if (m_wroteOurs) {
            std::string current;
            if (ReadUtf8File(m_pointer, current) && current != WideToUtf8(m_ourContent)) {
                // Somebody rewrote it while we held the lease. Theirs is newer than
                // ours; do not clobber it. Leave the backup for manual recovery.
                m_log->A("ReloadedPointerLease: pointer changed externally; preserving it "
                         "and leaving our backup in place");
            } else {
                RestoreFromBackup();
            }
        }
        if (m_ownsMutex) ReleaseMutex(m_mutex);
        if (m_mutex) CloseHandle(m_mutex);
    }

    ReloadedPointerLease(const ReloadedPointerLease&) = delete;
    ReloadedPointerLease& operator=(const ReloadedPointerLease&) = delete;

    bool Ok() const { return m_error.empty(); }
    const std::wstring& Error() const { return m_error; }

private:
    Logger* m_log = nullptr;
    fs::path m_pointer;
    fs::path m_backup;
    std::wstring m_ourContent;
    std::wstring m_error;
    HANDLE m_mutex = nullptr;
    bool m_ownsMutex = false;
    bool m_wroteOurs = false;
    bool m_hadOriginal = false;

    void Fail(const std::wstring& message) {
        m_error = message;
        if (m_log) m_log->W(L"ReloadedPointerLease: " + message);
    }

    // FNV-1a over the lowercased pointer path, matching Blind Soldier exactly so
    // both mods derive the same mutex name for the same file.
    static unsigned long long HashPath(const std::wstring& path) {
        unsigned long long hash = 1469598103934665603ULL;
        for (wchar_t character : ToLower(path)) {
            hash ^= (unsigned long long)character;
            hash *= 1099511628211ULL;
        }
        return hash;
    }

    // A backup left behind means a previous run of THIS mod died without
    // restoring. Put it back before touching anything, unless the live pointer
    // has since been replaced by something that is not ours.
    bool RecoverLeftoverBackup() {
        std::error_code ec;
        if (!fs::exists(m_backup, ec)) return true;

        if (fs::exists(m_pointer, ec)) {
            std::string current;
            if (ReadUtf8File(m_pointer, current) && current != WideToUtf8(m_ourContent)) {
                m_log->A("ReloadedPointerLease: a leftover backup and a foreign pointer both "
                         "exist; preserving the foreign pointer and keeping the backup");
                Fail(L"A previous session left a Reloaded pointer backup and the live pointer "
                     L"has since been changed by something else. Restore it manually before "
                     L"launching again.");
                return false;
            }
        }

        if (!MoveFileExW(m_backup.c_str(), m_pointer.c_str(),
                         MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
            Fail(L"Could not recover the leftover Reloaded pointer backup: " +
                 Logger::FormatWin32Error(GetLastError()));
            return false;
        }
        m_log->A("ReloadedPointerLease: recovered a leftover backup from a previous session");
        return true;
    }

    void RestoreFromBackup() {
        std::error_code ec;
        if (m_hadOriginal && fs::exists(m_backup, ec)) {
            if (MoveFileExW(m_backup.c_str(), m_pointer.c_str(),
                            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
                m_log->A("ReloadedPointerLease: original pointer restored");
            } else {
                m_log->Err(L"ReloadedPointerLease: restore original", GetLastError());
            }
        } else {
            fs::remove(m_pointer, ec);
            m_log->A("ReloadedPointerLease: removed our pointer (no original existed)");
        }
        m_wroteOurs = false;
    }
};

} // namespace cta
