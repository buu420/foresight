#pragma once
#include "common.h"
using namespace cta;
constexpr DWORD INJECT_TIMEOUT_MS = 60000;
constexpr DWORD MODULE_WAIT_MS = 10000;

// =========================================================================
// Remote export resolution
//
// Never assume a local function address is valid in the target. LoadLibraryW may
// live in kernelbase.dll rather than kernel32.dll, and ASLR can rebase either.
// Resolve the owning module locally, take the function's RVA inside it, then add
// that RVA to the SAME module's base in the target process.
// =========================================================================

static bool WaitForRemoteModuleBase(DWORD processId, const std::wstring& moduleName,
                                    HANDLE process, uintptr_t& outBase, Logger& log) {
    // GetTickCount64 rather than GetTickCount: the 32-bit counter wraps after about
    // 49.7 days of uptime, which would make this deadline compare wrongly.
    ULONGLONG deadline = GetTickCount64() + MODULE_WAIT_MS;
    for (;;) {
        HANDLE snapshot = CreateToolhelp32Snapshot(
            TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, processId);
        if (snapshot != INVALID_HANDLE_VALUE) {
            MODULEENTRY32W entry{};
            entry.dwSize = sizeof(entry);
            if (Module32FirstW(snapshot, &entry)) {
                do {
                    if (_wcsicmp(entry.szModule, moduleName.c_str()) == 0) {
                        outBase = (uintptr_t)entry.modBaseAddr;
                        CloseHandle(snapshot);
                        return true;
                    }
                } while (Module32NextW(snapshot, &entry));
            }
            CloseHandle(snapshot);
        }

        if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
            log.W(L"WaitForRemoteModuleBase: target exited while waiting for " + moduleName);
            return false;
        }
        if (GetTickCount64() >= deadline) {
            log.W(L"WaitForRemoteModuleBase: timed out waiting for " + moduleName);
            return false;
        }
        Sleep(25);
    }
}

static LPTHREAD_START_ROUTINE ResolveRemoteLoadLibraryW(HANDLE process, DWORD processId,
                                                       Logger& log) {
    HMODULE localKernel32 = GetModuleHandleW(L"kernel32.dll");
    FARPROC localFn = localKernel32 ? GetProcAddress(localKernel32, "LoadLibraryW") : nullptr;
    if (!localFn) {
        log.Err(L"ResolveRemoteLoadLibraryW: GetProcAddress(LoadLibraryW)", GetLastError());
        return nullptr;
    }

    // Which module does that address really belong to? Exports forward.
    HMODULE owner = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                            (LPCWSTR)localFn, &owner) || !owner) {
        log.Err(L"ResolveRemoteLoadLibraryW: GetModuleHandleExW", GetLastError());
        return nullptr;
    }

    std::vector<wchar_t> ownerPath(MAX_PATH * 4);
    DWORD n = GetModuleFileNameW(owner, ownerPath.data(), (DWORD)ownerPath.size());
    if (n == 0) {
        log.Err(L"ResolveRemoteLoadLibraryW: GetModuleFileNameW", GetLastError());
        return nullptr;
    }
    std::wstring ownerName = fs::path(std::wstring(ownerPath.data(), n)).filename().wstring();
    uintptr_t rva = (uintptr_t)localFn - (uintptr_t)owner;

    uintptr_t remoteBase = 0;
    if (!WaitForRemoteModuleBase(processId, ownerName, process, remoteBase, log))
        return nullptr;

    uintptr_t remoteFn = remoteBase + rva;
    log.W(L"ResolveRemoteLoadLibraryW: " + ownerName +
          L" localBase=0x" + std::to_wstring((uintptr_t)owner) +
          L" remoteBase=0x" + std::to_wstring(remoteBase) +
          L" rva=0x" + std::to_wstring(rva));
    return (LPTHREAD_START_ROUTINE)remoteFn;
}

// =========================================================================
// DLL injection via remote LoadLibraryW
// =========================================================================

enum class InjectResult {
    Success, AllocFailed, WriteFailed, ResolveFailed,
    CreateThreadFailed, TimedOut, LoadLibraryFailed,
};

static const wchar_t* InjectResultName(InjectResult r) {
    switch (r) {
        case InjectResult::Success:            return L"Success";
        case InjectResult::AllocFailed:        return L"VirtualAllocEx failed";
        case InjectResult::WriteFailed:        return L"WriteProcessMemory failed";
        case InjectResult::ResolveFailed:      return L"could not resolve LoadLibraryW in the game";
        case InjectResult::CreateThreadFailed: return L"CreateRemoteThread failed";
        case InjectResult::TimedOut:           return L"loading the mod loader timed out";
        case InjectResult::LoadLibraryFailed:  return L"the mod loader DLL failed to load";
        default:                               return L"unknown";
    }
}

static InjectResult InjectDll(HANDLE process, DWORD processId,
                              const std::wstring& dllPath, Logger& log) {
    size_t bytes = (dllPath.size() + 1) * sizeof(wchar_t);
    LPVOID remote = VirtualAllocEx(process, nullptr, bytes,
                                   MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) {
        log.Err(L"InjectDll: VirtualAllocEx", GetLastError());
        return InjectResult::AllocFailed;
    }

    if (!WriteProcessMemory(process, remote, dllPath.c_str(), bytes, nullptr)) {
        log.Err(L"InjectDll: WriteProcessMemory", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return InjectResult::WriteFailed;
    }

    LPTHREAD_START_ROUTINE loadLibrary = ResolveRemoteLoadLibraryW(process, processId, log);
    if (!loadLibrary) {
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return InjectResult::ResolveFailed;
    }

    HANDLE thread = CreateRemoteThread(process, nullptr, 0, loadLibrary, remote, 0, nullptr);
    if (!thread) {
        log.Err(L"InjectDll: CreateRemoteThread", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return InjectResult::CreateThreadFailed;
    }
    log.W(L"InjectDll: remote thread started, waiting up to " +
          std::to_wstring(INJECT_TIMEOUT_MS) + L"ms");

    DWORD elapsed = 0;
    const DWORD tick = 5000;
    for (;;) {
        DWORD w = WaitForSingleObject(thread, tick);
        if (w == WAIT_OBJECT_0) break;
        if (w == WAIT_TIMEOUT) {
            elapsed += tick;
            log.W(L"InjectDll: still waiting... " + std::to_wstring(elapsed) + L"ms");
            if (elapsed >= INJECT_TIMEOUT_MS) {
                CloseHandle(thread);
                // The remote allocation is deliberately NOT freed here. The remote
                // thread is still running and LoadLibraryW may still be reading the
                // path string out of it; unmapping it underneath would fault inside
                // the game. Leaking one page is the lesser harm.
                return InjectResult::TimedOut;
            }
            continue;
        }
        log.Err(L"InjectDll: WaitForSingleObject", GetLastError());
        CloseHandle(thread);
        // Same reasoning as the timeout path: the thread's state is unknown.
        return InjectResult::CreateThreadFailed;
    }

    DWORD moduleHandle = 0;
    BOOL readExit = GetExitCodeThread(thread, &moduleHandle);
    CloseHandle(thread);
    VirtualFreeEx(process, remote, 0, MEM_RELEASE);

    if (!readExit || moduleHandle == 0) {
        log.A("InjectDll: remote LoadLibraryW returned NULL");
        return InjectResult::LoadLibraryFailed;
    }
    log.W(L"InjectDll: loaded, remote HMODULE=0x" + std::to_wstring(moduleHandle));
    return InjectResult::Success;
}
