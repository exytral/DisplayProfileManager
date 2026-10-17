#pragma once
#include <string>

// Windows CommandLineToArgvW-compatible quoting.
inline std::wstring QuoteWindowsArgument(const std::wstring& value)
{
    std::wstring result = L"\"";
    size_t slashes = 0;
    for (wchar_t c : value)
    {
        if (c == L'\\') { ++slashes; continue; }
        if (c == L'"')
        {
            result.append(slashes * 2 + 1, L'\\');
            result += L'"';
        }
        else
        {
            result.append(slashes, L'\\');
            result += c;
        }
        slashes = 0;
    }
    result.append(slashes * 2, L'\\');
    result += L'"';
    return result;
}

inline std::wstring BuildProfileArguments(const std::wstring& profileId, bool developerRouting)
{
    return (developerRouting ? L"--dev " : L"")
        + std::wstring(L"--headless ") + QuoteWindowsArgument(profileId)
        + L" --source DesktopContextMenu";
}
