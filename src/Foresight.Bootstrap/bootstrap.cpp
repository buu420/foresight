// Registry-free attach broker. Its lifetime owns the shared Reloaded pointer lease.
#include "../ChronoTriggerAccessibility.Launcher/injection.h"

struct Handle {
    HANDLE value = nullptr;
    explicit Handle(HANDLE v) : value(v) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};

static int Attach(Logger& log) {
    int count = 0;
    LPWSTR* arguments = CommandLineToArgvW(GetCommandLineW(), &count);
    if (!arguments) return 2;
    std::vector<std::wstring> args(arguments, arguments + count);
    LocalFree(arguments);
    if (args.size() != 5 || args[1] != L"--pid" || args[3] != L"--ready-event" ||
        args[2].empty() || args[2].find_first_not_of(L"0123456789") != std::wstring::npos)
        throw std::runtime_error("Invalid attach arguments");
    const unsigned long long parsed = std::stoull(args[2]);
    if (!parsed || parsed > MAXDWORD) throw std::runtime_error("Invalid game process ID");
    const DWORD pid = static_cast<DWORD>(parsed);
    const std::wstring prefix = L"Local\\Foresight.Bootstrap." + std::to_wstring(pid) + L".";
    GUID launch{};
    const auto& event = args[4];
    if (event.size() != prefix.size() + 38 || event.compare(0, prefix.size(), prefix) != 0 ||
        FAILED(CLSIDFromString(event.c_str() + prefix.size(), &launch)))
        throw std::runtime_error("Invalid private readiness event");
    Handle ready(OpenEventW(EVENT_MODIFY_STATE, FALSE, event.c_str()));
    if (!ready.value) throw std::runtime_error("Readiness event is absent");
    const fs::path root = SelfDir().parent_path().parent_path();
    const fs::path game = root / GAME_EXE_NAME;
    std::wstring hash;
    if (!FileSha256(game, hash) || hash != SUPPORTED_GAME_SHA256)
        throw std::runtime_error("Unsupported game executable");
    Handle process(OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION |
        PROCESS_VM_WRITE | PROCESS_VM_READ | SYNCHRONIZE, FALSE, pid));
    if (!process.value) throw std::runtime_error("Cannot open game process");
    wchar_t image[32768]{}; DWORD size = _countof(image);
    if (!QueryFullProcessImageNameW(process.value, 0, image, &size) || !fs::equivalent(game, fs::path(image)))
        throw std::runtime_error("Process ID does not identify this installed game");
    BOOL targetWow = FALSE, selfWow = FALSE;
    if (!IsWow64Process(process.value, &targetWow) || !IsWow64Process(GetCurrentProcess(), &selfWow) || targetWow != selfWow)
        throw std::runtime_error("Game process architecture is not x86");
    const std::wstring mutexName = L"Local\\Foresight.Attach." + std::to_wstring(pid);
    Handle singleton(CreateMutexW(nullptr, FALSE, mutexName.c_str()));
    if (!singleton.value || GetLastError() == ERROR_ALREADY_EXISTS)
        throw std::runtime_error("Another Foresight helper already owns this game process");
    PortableLayout layout(root);
    if (!layout.FindMissing().empty()) throw std::runtime_error("Incomplete Foresight payload");
    ReloadedPointerLease lease(layout.reloadedRoot, log);
    if (!lease.Ok()) throw std::runtime_error(WideToUtf8(lease.Error()));
    if (!WriteAppConfig(layout.reloadedRoot, game, log)) throw std::runtime_error("Cannot write portable game configuration");
    if (WaitForSingleObject(process.value, 0) != WAIT_TIMEOUT) throw std::runtime_error("Game exited before injection");
    const InjectResult result = InjectDll(process.value, pid, layout.bootstrapperDll, log);
    if (result != InjectResult::Success) {
        log.W(L"Injection failed: " + std::wstring(InjectResultName(result)));
        // A timed-out remote LoadLibrary may still run. Keep its configuration
        // available until the proxy closes the failed game, then restore it.
        if (result == InjectResult::TimedOut) WaitForSingleObject(process.value, INFINITE);
        return 1;
    }
    if (WaitForSingleObject(process.value, 0) != WAIT_TIMEOUT || !SetEvent(ready.value))
        throw std::runtime_error("Game exited or readiness signal failed");
    log.A("Bootstrapper loaded; holding pointer lease until game exit.");
    WaitForSingleObject(process.value, INFINITE);
    log.A("Game exited; restoring Reloaded pointer.");
    return 0;
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, LPWSTR, int) {
    Logger log;
    log.Open(SelfDir().parent_path() / L"Logs", L"bootstrap.log");
    int result = 1;
    try { result = Attach(log); }
    catch (const std::exception& error) { log.A(error.what()); }
    log.Close();
    return result;
}
