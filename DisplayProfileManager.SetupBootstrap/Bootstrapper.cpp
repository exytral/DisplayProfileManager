#define WIN32_LEAN_AND_MEAN
#include "BootstrapPolicy.h"
#include "resource.h"

#include <string>
#include <vector>

#include <windows.h>
#include <commctrl.h>
#include <objbase.h>
#include <shellapi.h>
#include <sddl.h>
#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")

namespace
{
#if defined(_M_X64)
    constexpr wchar_t LegacyAppId[] = L"{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-x64}";
    constexpr wchar_t CurrentUserAppId[] = L"{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-x64-currentuser}";
    constexpr wchar_t AllUsersAppId[] = L"{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-x64-allusers}";
    constexpr REGSAM RegistryView = KEY_WOW64_64KEY;
#elif defined(_M_ARM64)
    constexpr wchar_t LegacyAppId[] = L"{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-arm64}";
    constexpr wchar_t CurrentUserAppId[] = L"{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-arm64-currentuser}";
    constexpr wchar_t AllUsersAppId[] = L"{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-arm64-allusers}";
    constexpr REGSAM RegistryView = KEY_WOW64_64KEY;
#else
    constexpr wchar_t LegacyAppId[] = L"{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-x86}";
    constexpr wchar_t CurrentUserAppId[] = L"{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-x86-currentuser}";
    constexpr wchar_t AllUsersAppId[] = L"{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-x86-allusers}";
    constexpr REGSAM RegistryView = KEY_WOW64_32KEY;
#endif

    constexpr int CurrentUserButton = 1001;
    constexpr int AllUsersButton = 1002;

    bool RegistrationKeyExists(HKEY root, const wchar_t* appId)
    {
        const std::wstring path =
            L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" +
            std::wstring(appId) + L"_is1";

        HKEY key = nullptr;
        const LONG result = RegOpenKeyExW(root, path.c_str(), 0, KEY_READ | RegistryView, &key);
        if (key)
            RegCloseKey(key);
        return result == ERROR_SUCCESS;
    }

    bool RegistrationExists(HKEY root, const wchar_t* scopedAppId)
    {
        return RegistrationKeyExists(root, scopedAppId) ||
            RegistrationKeyExists(root, LegacyAppId);
    }

    bool IsProcessElevated()
    {
        HANDLE token = nullptr;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
        {
            return false;
        }

        TOKEN_ELEVATION elevation{};
        DWORD size = 0;
        const bool ok = GetTokenInformation(
            token, TokenElevation, &elevation, sizeof(elevation), &size) != FALSE;
        CloseHandle(token);
        return ok && elevation.TokenIsElevated != 0;
    }

    DWORD RelaunchElevated(const std::vector<std::wstring>& publicArguments)
    {
        wchar_t modulePath[MAX_PATH + 1]{};
        if (!GetModuleFileNameW(nullptr, modulePath, ARRAYSIZE(modulePath)))
        {
            return GetLastError();
        }

        const auto elevationArguments = BuildElevationArguments(publicArguments);
        const std::wstring parameterTail = BuildArgumentTail(elevationArguments);

        SHELLEXECUTEINFOW info{};
        info.cbSize = sizeof(info);
        info.fMask = SEE_MASK_NOCLOSEPROCESS;
        info.lpVerb = L"runas";
        info.lpFile = modulePath;
        info.lpParameters = parameterTail.c_str();
        info.nShow = SW_SHOWNORMAL;

        if (!ShellExecuteExW(&info))
        {
            return GetLastError();
        }

        if (!info.hProcess)
        {
            return ERROR_GEN_FAILURE;
        }

        const DWORD wait = WaitForSingleObject(info.hProcess, INFINITE);
        DWORD exitCode = ERROR_GEN_FAILURE;
        if (wait == WAIT_OBJECT_0)
            GetExitCodeProcess(info.hProcess, &exitCode);
        CloseHandle(info.hProcess);
        return exitCode;
    }

    SetupScope ShowScopeChooser(InstallState state)
    {
        const ChoiceLabels labels = BuildChoiceLabels(state);
        const TASKDIALOG_BUTTON buttons[] = {
            { CurrentUserButton, labels.currentUser.c_str() },
            { AllUsersButton, labels.allUsers.c_str() },
        };

        TASKDIALOGCONFIG config{};
        config.cbSize = sizeof(config);
        config.hInstance = GetModuleHandleW(nullptr);
        config.dwFlags = TDF_ALLOW_DIALOG_CANCELLATION | TDF_SIZE_TO_CONTENT;
        config.dwCommonButtons = TDCBF_CANCEL_BUTTON;
        config.pszWindowTitle = L"Display Profile Manager Setup";
        config.pszMainIcon = MAKEINTRESOURCEW(IDI_APP_ICON);
        config.pszMainInstruction = nullptr;
        config.pszContent =
            L"Choose whether to install for just you or everyone who uses this PC.";
        config.cButtons = ARRAYSIZE(buttons);
        config.pButtons = buttons;
        config.nDefaultButton =
            GetDefaultChoice(state) == DefaultChoice::CurrentUser ? CurrentUserButton : IDCANCEL;

        int button = IDCANCEL;
        if (FAILED(TaskDialogIndirect(&config, &button, nullptr, nullptr)))
        {
            return SetupScope::None;
        }

        if (button == CurrentUserButton)
        {
            return SetupScope::CurrentUser;
        }
        if (button == AllUsersButton)
        {
            return SetupScope::AllUsers;
        }
        return SetupScope::None;
    }

    bool WriteAll(HANDLE file, const BYTE* data, DWORD size)
    {
        DWORD offset = 0;
        while (offset < size)
        {
            DWORD written = 0;
            if (!WriteFile(file, data + offset, size - offset, &written, nullptr) || written == 0)
            {
                return false;
            }
            offset += written;
        }
        return true;
    }

    bool CreateStagingSecurityDescriptor(PSECURITY_DESCRIPTOR& securityDescriptor)
    {
        securityDescriptor = nullptr;
        HANDLE token = nullptr;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
            return false;
        DWORD size = 0;
        GetTokenInformation(token, TokenUser, nullptr, 0, &size);
        std::vector<BYTE> buffer(size);
        const bool gotUser = size != 0 && GetTokenInformation(token, TokenUser, buffer.data(), size, &size);
        TOKEN_ELEVATION elevation{};
        DWORD elevationSize = 0;
        const bool gotElevation = GetTokenInformation(
            token, TokenElevation, &elevation, sizeof(elevation), &elevationSize) != FALSE;
        CloseHandle(token);
        if (!gotUser || !gotElevation)
            return false;

        const auto* user = reinterpret_cast<const TOKEN_USER*>(buffer.data());
        LPWSTR sid = nullptr;
        if (!ConvertSidToStringSidW(user->User.Sid, &sid))
            return false;
        const std::wstring descriptor = elevation.TokenIsElevated
            ? L"O:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)"
            : L"D:P(A;;FA;;;SY)(A;;FA;;;" + std::wstring(sid) + L")";
        LocalFree(sid);
        return ConvertStringSecurityDescriptorToSecurityDescriptorW(
            descriptor.c_str(), SDDL_REVISION_1, &securityDescriptor, nullptr) != FALSE;
    }

    bool CreateProtectedStagingDirectory(const std::wstring& path, HANDLE& guard)
    {
        guard = INVALID_HANDLE_VALUE;
        PSECURITY_DESCRIPTOR securityDescriptor = nullptr;
        if (!CreateStagingSecurityDescriptor(securityDescriptor))
            return false;
        SECURITY_ATTRIBUTES attributes{sizeof(attributes), securityDescriptor, FALSE};
        const bool created = CreateDirectoryW(path.c_str(), &attributes) != FALSE;
        LocalFree(securityDescriptor);
        if (!created)
            return false;

        // Keep DELETE sharing denied through process creation and completion.
        // A writable TEMP parent alone must not permit renaming this directory.
        guard = CreateFileW(path.c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ,
            nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
        if (guard == INVALID_HANDLE_VALUE)
            return false;
        BY_HANDLE_FILE_INFORMATION info{};
        return GetFileInformationByHandle(guard, &info) &&
            !(info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT);
    }

    bool ExtractPayload(std::wstring& directory, std::wstring& executable, HANDLE& directoryGuard)
    {
        wchar_t tempPath[MAX_PATH + 1]{};
        if (!GetTempPathW(ARRAYSIZE(tempPath), tempPath))
        {
            return false;
        }

        GUID guid{};
        if (FAILED(CoCreateGuid(&guid)))
        {
            return false;
        }

        wchar_t guidText[64]{};
        if (!StringFromGUID2(guid, guidText, ARRAYSIZE(guidText)))
        {
            return false;
        }

        directory = std::wstring(tempPath) + L"DPMSetup-" + guidText;
        for (auto& ch : directory)
        {
            if (ch == L'{' || ch == L'}')
                ch = L'-';
        }

        if (!CreateProtectedStagingDirectory(directory, directoryGuard))
        {
            return false;
        }

        executable = directory + L"\\DisplayProfileManager.Payload.exe";
        HRSRC resource = FindResourceW(nullptr, MAKEINTRESOURCEW(IDR_SETUP_PAYLOAD), RT_RCDATA);
        if (!resource)
        {
            return false;
        }

        HGLOBAL loaded = LoadResource(nullptr, resource);
        const DWORD size = SizeofResource(nullptr, resource);
        const auto* data = static_cast<const BYTE*>(LockResource(loaded));
        if (!loaded || !data || size == 0)
        {
            return false;
        }

        // The executable must not receive the default token-owned child DACL.
        // Secure the file at CREATE_NEW, before any payload bytes are written.
        PSECURITY_DESCRIPTOR payloadSecurity = nullptr;
        if (!CreateStagingSecurityDescriptor(payloadSecurity))
            return false;
        SECURITY_ATTRIBUTES payloadAttributes{sizeof(payloadAttributes), payloadSecurity, FALSE};
        HANDLE file = CreateFileW(
            executable.c_str(), GENERIC_WRITE, 0, &payloadAttributes, CREATE_NEW,
            FILE_ATTRIBUTE_NORMAL, nullptr);
        LocalFree(payloadSecurity);
        if (file == INVALID_HANDLE_VALUE)
        {
            return false;
        }

        const bool ok = WriteAll(file, data, size) && FlushFileBuffers(file);
        CloseHandle(file);
        return ok;
    }

    DWORD RunPayload(const std::wstring& executable, const std::vector<std::wstring>& arguments)
    {
        std::wstring commandLine = BuildChildCommandLine(executable, arguments);
        std::vector<wchar_t> mutableLine(commandLine.begin(), commandLine.end());
        mutableLine.push_back(L'\0');

        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process{};

        if (!CreateProcessW(
            executable.c_str(), mutableLine.data(), nullptr, nullptr, FALSE, 0,
            nullptr, nullptr, &startup, &process))
            return GetLastError();

        CloseHandle(process.hThread);
        const DWORD wait = WaitForSingleObject(process.hProcess, INFINITE);
        DWORD exitCode = ERROR_GEN_FAILURE;
        if (wait == WAIT_OBJECT_0)
            GetExitCodeProcess(process.hProcess, &exitCode);
        CloseHandle(process.hProcess);
        return exitCode;
    }
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv)
    {
        return ERROR_INVALID_PARAMETER;
    }

    std::vector<std::wstring> arguments;
    for (int i = 1; i < argc; ++i)
        arguments.emplace_back(argv[i]);
    LocalFree(argv);

    ArgumentPolicy policy = EvaluateArguments(arguments);
    if (!policy.valid)
    {
        return ERROR_INVALID_PARAMETER;
    }

    SetupScope scope = policy.explicitScope;
    if (policy.showChooser)
    {
        scope = ShowScopeChooser({
            RegistrationExists(HKEY_CURRENT_USER, CurrentUserAppId),
            RegistrationExists(HKEY_LOCAL_MACHINE, AllUsersAppId),
        });
        if (scope == SetupScope::None)
        {
            return ERROR_CANCELLED;
        }
        arguments = ApplySelectedScope(policy, scope);
    }
    else
    {
        arguments = policy.forwarded;
    }

    const bool elevated = IsProcessElevated();
    switch (DecideBootstrapRoute(scope, elevated, policy.internalElevatedAllUsers))
    {
    case BootstrapRoute::RejectElevationRequired:
        return ERROR_ELEVATION_REQUIRED;

    case BootstrapRoute::RejectInvalid:
        return ERROR_INVALID_PARAMETER;

    case BootstrapRoute::SelfElevate:
        return static_cast<int>(RelaunchElevated(arguments));

    case BootstrapRoute::RunPayload:
        break;
    }

    std::wstring tempDirectory;
    std::wstring payload;
    HANDLE directoryGuard = INVALID_HANDLE_VALUE;
    if (!ExtractPayload(tempDirectory, payload, directoryGuard))
    {
        if (!payload.empty())
            DeleteFileW(payload.c_str());
        if (directoryGuard != INVALID_HANDLE_VALUE)
            CloseHandle(directoryGuard);
        if (!tempDirectory.empty())
            RemoveDirectoryW(tempDirectory.c_str());
        return ERROR_CANNOT_MAKE;
    }

    const DWORD exitCode = RunPayload(payload, arguments);
    DeleteFileW(payload.c_str());
    if (directoryGuard != INVALID_HANDLE_VALUE)
        CloseHandle(directoryGuard);
    RemoveDirectoryW(tempDirectory.c_str());
    return static_cast<int>(exitCode);
}
