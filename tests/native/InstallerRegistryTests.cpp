// Exercise the production registry functions against a disposable HKCU tree.
// No real Image File Execution Options values are read or changed.
#include "../../src/ChronoTriggerAccessibility.Launcher/common.h"
#include <iostream>
static int WINAPI TestMessageBoxW(HWND, LPCWSTR, LPCWSTR, UINT) { return IDYES; }
#define MessageBoxW TestMessageBoxW
#define wWinMain UnusedInstallerEntry
#include "../../src/ChronoTriggerAccessibility.Installer/installer.cpp"
#undef wWinMain
#undef MessageBoxW

int main() {
    const std::wstring testPath = L"Software\\ForesightInstallerTests-" + std::to_wstring(GetCurrentProcessId());
    HKEY testRoot = nullptr;
    if (RegCreateKeyExW(HKEY_CURRENT_USER, testPath.c_str(), 0, nullptr, 0,
                        KEY_ALL_ACCESS, nullptr, &testRoot, nullptr) != ERROR_SUCCESS) return 2;
    if (RegOverridePredefKey(HKEY_LOCAL_MACHINE, testRoot) != ERROR_SUCCESS) return 3;
    int failures = 0;
    auto check = [&](bool condition, const char* name) {
        std::cout << (condition ? "PASS " : "FAIL ") << name << '\n';
        if (!condition) ++failures;
    };
    Logger log;
    const fs::path launcher = fs::temp_directory_path() / L"Foresight Test Game" / L"Accessibility" / L"Launcher" / L"ChronoTriggerAccessibility.Launcher.exe";
    const std::wstring expected = L"\"" + launcher.wstring() + L"\"";
    HKEY key = nullptr;
    auto read = [&](const wchar_t* name) {
        std::wstring value;
        ReadStringValue(key, name, value);
        return value;
    };
    auto write = [&](const wchar_t* name, const std::wstring& value) {
        return RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()),
                            static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t))) == ERROR_SUCCESS;
    };
    check(RegisterIfeo(launcher, log) == ERROR_SUCCESS, "fresh install");
    RegOpenKeyExW(HKEY_LOCAL_MACHINE, IfeoKeyPath().c_str(), 0, KEY_ALL_ACCESS | KEY_WOW64_64KEY, &key);
    check(read(L"Debugger") == expected && read(OWNER_VALUE) == expected, "matching ownership values");
    check(RegisterIfeo(launcher, log) == ERROR_SUCCESS, "idempotent update");
    const std::wstring foreign = L"\"" + (fs::temp_directory_path() / L"Another Tool" / L"debugger.exe").wstring() + L"\"";
    check(write(L"Debugger", foreign), "set foreign replacement");
    check(RegisterIfeo(launcher, log) == ERROR_ALREADY_EXISTS, "install refuses replaced debugger with stale owner");
    check(read(L"Debugger") == foreign, "foreign redirect preserved by install");
    check(UnregisterIfeo(log) == ERROR_ACCESS_DENIED, "uninstall refuses replaced debugger with stale owner");
    check(read(L"Debugger") == foreign && read(OWNER_VALUE) == expected, "foreign redirect and stale marker preserved");
    check(write(L"Debugger", expected), "restore owned redirect");
    check(write(L"UnrelatedTestValue", L"preserve this"), "set unrelated IFEO value");
    RegCloseKey(key); key = nullptr;
    check(UnregisterIfeo(log) == ERROR_SUCCESS, "remove owned redirect");
    check(RegOpenKeyExW(HKEY_LOCAL_MACHINE, IfeoKeyPath().c_str(), 0, KEY_ALL_ACCESS | KEY_WOW64_64KEY, &key) == ERROR_SUCCESS,
          "preserve IFEO key with unrelated settings");
    check(read(L"UnrelatedTestValue") == L"preserve this", "preserve unrelated IFEO value");
    if (key) RegCloseKey(key); key = nullptr;
    check(UnregisterIfeo(log) == ERROR_SUCCESS, "idempotent removal");
    RegOverridePredefKey(HKEY_LOCAL_MACHINE, nullptr);
    RegCloseKey(testRoot);
    RegDeleteTreeW(HKEY_CURRENT_USER, testPath.c_str());
    return failures ? 1 : 0;
}
