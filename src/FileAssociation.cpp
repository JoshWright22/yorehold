#include "FileAssociation.h"

#ifdef _WIN32

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shlobj.h>

#include <string>

namespace
{

// Returns true if the value had to be written.
bool setValue(const std::wstring& key, const wchar_t* name, const std::wstring& value)
{
    wchar_t current[1024];
    DWORD size = sizeof(current);
    if (RegGetValueW(HKEY_CURRENT_USER, key.c_str(), name, RRF_RT_REG_SZ, nullptr, current, &size) == ERROR_SUCCESS && value == current)
        return false;
    return RegSetKeyValueW(HKEY_CURRENT_USER, key.c_str(), name, REG_SZ, value.c_str(),
        static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t))) == ERROR_SUCCESS;
}

}

void registerYoreFiles()
{
    wchar_t exe[MAX_PATH];
    const DWORD length = GetModuleFileNameW(nullptr, exe, MAX_PATH);
    if (length == 0 || length == MAX_PATH)
        return;
    const std::wstring path = exe;
    const std::wstring classes = L"Software\\Classes\\";
    bool changed = setValue(classes + L".yore", nullptr, L"Yorehold.Content");
    changed |= setValue(classes + L"Yorehold.Content", nullptr, L"Yorehold content");
    changed |= setValue(classes + L"Yorehold.Content\\DefaultIcon", nullptr, L"\"" + path + L"\",0");
    changed |= setValue(classes + L"Yorehold.Content\\shell\\open\\command", nullptr, L"\"" + path + L"\" \"%1\"");
    if (changed)
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr); // Explorer rereads its file types
}

#else

void registerYoreFiles() {}

#endif
