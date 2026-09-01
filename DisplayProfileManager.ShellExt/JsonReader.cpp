#include "JsonReader.h"
#include <algorithm>
#include <cwctype>
#include <shlwapi.h>
#include <windows.h>

static std::wstring ReadFileW(const std::wstring& path)
{
    // Read UTF-8 bytes directly because shell extension has no JSON dependency
    HANDLE hFile = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hFile == INVALID_HANDLE_VALUE)
        return {};

    LARGE_INTEGER size{};
    if (!GetFileSizeEx(hFile, &size) || size.QuadPart == 0 || size.QuadPart > 10 * 1024 * 1024)
    {
        CloseHandle(hFile);
        return {};
    }

    std::string buf(static_cast<size_t>(size.QuadPart), '\0');
    DWORD read = 0;
    if (!ReadFile(
        hFile,
        buf.data(),
        static_cast<DWORD>(buf.size()),
        &read,
        nullptr) ||
        read != buf.size())
    {
        CloseHandle(hFile);
        return {};
    }

    CloseHandle(hFile);

    if (buf.size() >= 3 &&
        static_cast<unsigned char>(buf[0]) == 0xEF &&
        static_cast<unsigned char>(buf[1]) == 0xBB &&
        static_cast<unsigned char>(buf[2]) == 0xBF)
        buf.erase(0, 3);

    int wlen = MultiByteToWideChar(CP_UTF8, 0, buf.c_str(), static_cast<int>(buf.size()), nullptr, 0);
    if (wlen <= 0) return {};
    std::wstring out(wlen, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, buf.c_str(), static_cast<int>(buf.size()), out.data(), wlen);
    return out;
}

static std::wstring ExtractStringField(const std::wstring& json, const std::wstring& key)
{
    std::wstring needle = L"\"" + key + L"\"";
    size_t keyPos = json.find(needle);
    if (keyPos == std::wstring::npos)
        return {};

    size_t colon = json.find(L':', keyPos + needle.size());
    if (colon == std::wstring::npos)
        return {};

    size_t valStart = colon + 1;
    while (valStart < json.size() && iswspace(json[valStart]))
        ++valStart;

    if (valStart >= json.size())
        return {};

    if (json.compare(valStart, 4, L"null") == 0)
        return {};

    if (json[valStart] != L'"')
        return {};

    ++valStart;

    std::wstring result;
    result.reserve(64);
    for (size_t i = valStart; i < json.size(); ++i)
    {
        wchar_t c = json[i];
        if (c == L'\\' && i + 1 < json.size())
        {
            wchar_t esc = json[++i];
            switch (esc)
            {
            case L'"':  result += L'"';  break;
            case L'\\': result += L'\\'; break;
            case L'/':  result += L'/';  break;
            case L'n':  result += L'\n'; break;
            case L'r':  result += L'\r'; break;
            case L't':  result += L'\t'; break;
            default:    result += esc;   break;
            }
        }
        else if (c == L'"')
        {
            break;
        }
        else
        {
            result += c;
        }
    }
    return result;
}

std::wstring ReadCurrentProfileId(const std::wstring& settingsPath)
{
    auto json = ReadFileW(settingsPath);
    if (json.empty()) return {};
    return ExtractStringField(json, L"currentProfileId");
}

std::vector<ProfileEntry> ReadProfiles(const std::wstring& profilesDir)
{
    std::vector<ProfileEntry> profiles;

    std::wstring pattern = profilesDir + L"\\*.dpm";
    WIN32_FIND_DATAW fd{};
    HANDLE hFind = FindFirstFileW(pattern.c_str(), &fd);
    if (hFind == INVALID_HANDLE_VALUE)
        return profiles;

    do
    {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
            continue;

        std::wstring filePath = profilesDir + L"\\" + fd.cFileName;
        auto json = ReadFileW(filePath);
        if (json.empty())
            continue;

        ProfileEntry entry;
        entry.id   = ExtractStringField(json, L"id");
        entry.name = ExtractStringField(json, L"name");
        entry.icon = ExtractStringField(json, L"icon");

        if (entry.id.empty() || entry.name.empty())
            continue;

        profiles.push_back(std::move(entry));
    }
    while (FindNextFileW(hFind, &fd));

    FindClose(hFind);

    std::sort(profiles.begin(), profiles.end(),
        [](const ProfileEntry& a, const ProfileEntry& b)
        {
            return StrCmpLogicalW(a.name.c_str(), b.name.c_str()) < 0;
        });

    return profiles;
}