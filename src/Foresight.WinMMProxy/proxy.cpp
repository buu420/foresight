// File-based bootstrap. Forwarding is ready BEFORE any managed injection starts.
// Export list adapted from Buu420's Blind Soldier; startup deliberately does not
// use its historical WinMM wait-for-injection forwarding path.
#include "../ChronoTriggerAccessibility.Launcher/common.h"
using namespace cta;
extern "C" { FARPROC g_winmmExports[193] = {}; }
static HMODULE g_proxyModule = nullptr;
static volatile LONG g_forwardState = 0;

static fs::path ModulePath(HMODULE module) {
    wchar_t path[32768]{};
    DWORD size = GetModuleFileNameW(module, path, _countof(path));
    if (!size || size >= _countof(path)) throw std::runtime_error("Cannot resolve module path");
    return fs::path(path);
}

static void LoadSystemWinmm() {
    wchar_t directory[32768]{};
    UINT size = GetSystemWow64DirectoryW(directory, _countof(directory));
    fs::path system;
    if (size && size < _countof(directory) && directory[0]) system = directory;
    else {
        size = GetSystemDirectoryW(directory, _countof(directory));
        if (!size || size >= _countof(directory)) throw std::runtime_error("Cannot resolve Windows system directory");
        system = directory; // x86 process receives the system's x86 DLL view.
    }
    const fs::path systemWinmm = system / L"winmm.dll";
    HMODULE real = LoadLibraryW(systemWinmm.c_str());
    if (!real || real == g_proxyModule) throw std::runtime_error("Windows WinMM resolved to the proxy");
    if (!fs::equivalent(ModulePath(real), systemWinmm))
        throw std::runtime_error("Unexpected Windows WinMM module");
    for (WORD index = 0; index < 193; ++index) {
        FARPROC target = GetProcAddress(real, MAKEINTRESOURCEA(index + 2));
        MEMORY_BASIC_INFORMATION memory{};
        if (!target || !VirtualQuery(reinterpret_cast<LPCVOID>(target), &memory, sizeof(memory)) ||
            memory.AllocationBase == g_proxyModule)
            throw std::runtime_error("Missing or recursive Windows WinMM export");
        g_winmmExports[index] = target;
    }
    InterlockedExchange(&g_forwardState, 1);
}

static void StartAccessibility(Logger& log) {
    const fs::path game = SelfPath();
    if (ToLower(game.filename().wstring()) != ToLower(GAME_EXE_NAME)) return;
    const fs::path root = game.parent_path();
    if (!fs::equivalent(ModulePath(g_proxyModule).parent_path(), root))
        throw std::runtime_error("Foresight proxy must be beside the game");
    const fs::path runtime = root / L"Accessibility/Runtime/dotnet/x86";
    if (!fs::is_regular_file(runtime / L"host/fxr/9.0.20/hostfxr.dll"))
        throw std::runtime_error("Foresight private x86 runtime is missing");
    for (const wchar_t* name : {L"DOTNET_ROOT_X86", L"DOTNET_ROOT(x86)", L"DOTNET_ROOT"})
        if (!SetEnvironmentVariableW(name, runtime.c_str()))
            throw std::runtime_error("Cannot set process-local runtime path");
    GUID id{};
    wchar_t guid[40]{};
    if (FAILED(CoCreateGuid(&id)) || !StringFromGUID2(id, guid, _countof(guid)))
        throw std::runtime_error("Cannot create startup event identifier");
    const auto eventName = L"Local\\Foresight.Bootstrap." + std::to_wstring(GetCurrentProcessId()) + L"." + guid;
    HANDLE ready = CreateEventW(nullptr, TRUE, FALSE, eventName.c_str());
    if (!ready || GetLastError() == ERROR_ALREADY_EXISTS) {
        if (ready) CloseHandle(ready);
        throw std::runtime_error("Cannot create private startup event");
    }
    const auto managedName = L"Local\\Foresight.Managed." + std::to_wstring(GetCurrentProcessId()) + L"." + guid;
    HANDLE managedReady = CreateEventW(nullptr, TRUE, FALSE, managedName.c_str());
    if (!managedReady || GetLastError() == ERROR_ALREADY_EXISTS ||
        !SetEnvironmentVariableW(L"FORESIGHT_READY_EVENT", managedName.c_str())) {
        CloseHandle(ready);
        if (managedReady) CloseHandle(managedReady);
        throw std::runtime_error("Cannot create accessibility readiness event");
    }
    const fs::path helper = root / L"Accessibility/Bootstrap/Foresight.Bootstrap.exe";
    auto command = QuoteArgument(helper.wstring()) + L" --pid " + std::to_wstring(GetCurrentProcessId()) +
        L" --ready-event " + QuoteArgument(eventName);
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = SW_HIDE;
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(helper.c_str(), command.data(), nullptr, nullptr, FALSE,
                        CREATE_NO_WINDOW, nullptr, root.c_str(), &startup, &process)) {
        CloseHandle(ready); CloseHandle(managedReady);
        throw std::runtime_error("Cannot start Foresight.Bootstrap.exe");
    }
    CloseHandle(process.hThread);
    HANDLE waits[] = {ready, process.hProcess};
    DWORD result = WaitForMultipleObjects(2, waits, FALSE, 100000);
    CloseHandle(ready); CloseHandle(process.hProcess);
    if (result != WAIT_OBJECT_0) {
        CloseHandle(managedReady);
        throw std::runtime_error("Foresight bootstrap failed or timed out; see Accessibility/Logs/bootstrap.log");
    }
    log.A("Reloaded bootstrapper injected; waiting for Prism and accessibility hooks.");
    result = WaitForSingleObject(managedReady, 60000);
    CloseHandle(managedReady);
    SetEnvironmentVariableW(L"FORESIGHT_READY_EVENT", nullptr);
    if (result != WAIT_OBJECT_0) throw std::runtime_error("Foresight speech and hooks did not become ready; see the Reloaded log");
    log.A("Foresight accessibility runtime active. Windows API forwarding stayed independent.");
}

static DWORD WINAPI Initialize(void*) {
    Logger log;
    try {
        LoadSystemWinmm();
        if (ToLower(SelfPath().filename().wstring()) == ToLower(GAME_EXE_NAME))
            log.Open(SelfDir() / L"Accessibility/Logs", L"proxy.log");
        StartAccessibility(log);
    } catch (const std::exception& error) {
        if (InterlockedCompareExchange(&g_forwardState, 0, 0) == 0)
            InterlockedExchange(&g_forwardState, -1);
        log.A(error.what());
        MessageBoxW(nullptr, (L"Foresight could not start.\n\n" + Utf8ToWide(error.what()) +
            L"\n\nClose the game and reinstall the complete Foresight package.").c_str(),
            L"Foresight startup error", MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
        // Startup failure must not silently leave a blind player without speech.
        TerminateProcess(GetCurrentProcess(), 1);
    }
    log.Close();
    return 0;
}

extern "C" void __cdecl EnsureWinmmReady() {
    const ULONGLONG until = GetTickCount64() + 5000;
    LONG state;
    while ((state = InterlockedCompareExchange(&g_forwardState, 0, 0)) == 0 && GetTickCount64() < until) Sleep(1);
    if (state != 1) {
        OutputDebugStringW(L"Foresight: Windows WinMM forwarding could not initialize.\n");
        TerminateProcess(GetCurrentProcess(), 1);
    }
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_proxyModule = instance;
        // Static CRT: do not DisableThreadLibraryCalls. No loader work or waits here.
        HANDLE worker = CreateThread(nullptr, 0, Initialize, nullptr, 0, nullptr);
        if (!worker) return FALSE;
        CloseHandle(worker);
    }
    return TRUE;
}

#define FORESIGHT_WINMM_FORWARD(stub, index) \
extern "C" __declspec(naked) void stub() { \
    __asm pushfd \
    __asm pushad \
    __asm call EnsureWinmmReady \
    __asm popad \
    __asm popfd \
    __asm jmp dword ptr [g_winmmExports + index * 4] \
}
#include "winmm_exports.inc"
