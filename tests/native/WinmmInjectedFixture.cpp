#define WIN32_LEAN_AND_MEAN
#include <windows.h>
BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        auto timer = reinterpret_cast<DWORD(WINAPI*)()>(GetProcAddress(GetModuleHandleW(L"winmm.dll"), "timeGetTime"));
        if (!timer) return FALSE;
        timer(); // A forwarded call during injected initialization must not wait for injection completion.
    }
    return TRUE;
}
