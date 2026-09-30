// Cold-load the proxy first: preloading system WinMM would hide name recursion.
#include "../../src/ChronoTriggerAccessibility.Launcher/injection.h"
#include <mmsystem.h>
#include <cstdio>
int wmain(int argc, wchar_t** argv) {
    if (argc != 3) return 2;
    if (GetModuleHandleW(L"winmm.dll")) { puts("FAIL WinMM was already loaded"); return 1; }
    HMODULE proxy = LoadLibraryW(argv[1]);
    if (!proxy) { puts("FAIL portable WinMM proxy is missing or failed to load"); return 1; }
    for (WORD ordinal = 2; ordinal <= 194; ++ordinal)
        if (!GetProcAddress(proxy, MAKEINTRESOURCEA(ordinal))) { puts("FAIL missing ordinal"); return 1; }
    auto timer = reinterpret_cast<DWORD(WINAPI*)()>(GetProcAddress(proxy, "timeGetTime"));
    auto joys = reinterpret_cast<UINT(WINAPI*)()>(GetProcAddress(proxy, "joyGetNumDevs"));
    auto position = reinterpret_cast<MMRESULT(WINAPI*)(UINT, LPJOYINFOEX)>(GetProcAddress(proxy, "joyGetPosEx"));
    if (!timer || !joys || !position) return 1;
    DWORD first = timer(); // must return without an accessibility bootstrap in this non-game host.
    Sleep(30);
    DWORD elapsed = timer() - first;
    JOYINFOEX info{}; info.dwSize = sizeof(info); info.dwFlags = JOY_RETURNALL;
    for (int i = 0; i < 1000; ++i) { joys(); position(0, &info); timer(); }
    if (elapsed < 10 || elapsed > 3000) { puts("FAIL timeGetTime forwarding"); return 1; }
    puts("PASS cold-load ordinal surface and 3000 controller/timer calls");
    wchar_t systemDirectory[MAX_PATH]{};
    if (!GetSystemDirectoryW(systemDirectory, _countof(systemDirectory))) return 1;
    HMODULE system = LoadLibraryW((fs::path(systemDirectory) / L"winmm.dll").c_str());
    if (!system || system == proxy) return 1;
    const auto base = reinterpret_cast<const BYTE*>(system);
    const auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    const auto table = reinterpret_cast<const IMAGE_EXPORT_DIRECTORY*>(base + nt->OptionalHeader.DataDirectory[0].VirtualAddress);
    const auto names = reinterpret_cast<const DWORD*>(base + table->AddressOfNames);
    const auto ordinals = reinterpret_cast<const WORD*>(base + table->AddressOfNameOrdinals);
    if (table->NumberOfFunctions != 193 || table->NumberOfNames != 192 || table->Base != 2) return 1;
    for (DWORD i = 0; i < table->NumberOfNames; ++i) {
        auto byName = GetProcAddress(proxy, reinterpret_cast<const char*>(base + names[i]));
        auto byOrdinal = GetProcAddress(proxy, MAKEINTRESOURCEA(table->Base + ordinals[i]));
        if (!byName || byName != byOrdinal) { puts("FAIL WinMM export name/ordinal mapping"); return 1; }
    }
    puts("PASS all 192 export names and 193 ordinals match Windows WinMM");
    Logger log;
    if (InjectDll(GetCurrentProcess(), GetCurrentProcessId(), argv[2], log) != InjectResult::Success) {
        puts("FAIL forwarded WinMM call during injected DLL initialization"); return 1;
    }
    puts("PASS injected initialization can call WinMM without a readiness deadlock");
    return 0;
}
