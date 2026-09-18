#include "JsonReader.h"
#include <algorithm>
#include <cstdint>
#include <cwctype>
#include <shlwapi.h>
#include <string_view>
#include <windows.h>

namespace
{
    bool DecodeUtf8(const std::string& bytes, std::wstring& decoded)
    {
        decoded.clear();
        if (bytes.empty()) return false;

        int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), nullptr, 0);
        if (length <= 0) return false;

        decoded.resize(static_cast<size_t>(length));
        return MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), decoded.data(), length) == length;
    }

    bool ProfileBudgetAllows(size_t fileBytes, size_t aggregateBytes, size_t filesConsidered)
    {
        return fileBytes > 0 && fileBytes <= MaxJsonFileBytes && filesConsidered < MaxProfileFiles && aggregateBytes <= MaxProfileAggregateBytes - fileBytes;
    }

    bool ReadFileUtf8(const std::wstring& path, size_t maxBytes, std::wstring& decoded, size_t& bytesRead)
    {
        decoded.clear();
        bytesRead = 0;

        HANDLE hFile = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (hFile == INVALID_HANDLE_VALUE) return false;

        LARGE_INTEGER size{};
        if (!GetFileSizeEx(hFile, &size) || size.QuadPart <= 0 || static_cast<unsigned long long>(size.QuadPart) > maxBytes)
        {
            CloseHandle(hFile);
            return false;
        }

        std::string bytes(static_cast<size_t>(size.QuadPart), '\0');
        DWORD read = 0;
        bool readOk = ReadFile(hFile, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr) && read == bytes.size();
        CloseHandle(hFile);
        if (!readOk) return false;

        bytesRead = bytes.size();
        if (bytes.size() >= 3 && static_cast<unsigned char>(bytes[0]) == 0xEF && static_cast<unsigned char>(bytes[1]) == 0xBB && static_cast<unsigned char>(bytes[2]) == 0xBF)
            bytes.erase(0, 3);

        return DecodeUtf8(bytes, decoded);
    }

    class JsonParser
    {
    public:
        explicit JsonParser(std::wstring_view input) : _input(input) { }

        bool ParseProfile(ProfileEntry& entry)
        {
            entry = {};
            bool seenId = false, seenName = false, seenIcon = false;
            if (!BeginRootObject()) return false;

            if (Consume(L'}'))
            {
                return Finish() && false;
            }
            do
            {
                std::wstring key;
                if (!ParseString(key) || !ConsumeColon()) return false;

                if (key == L"id")
                {
                    if (seenId || !ParseString(entry.id)) return false;
                    seenId = true;
                }
                else if (key == L"name")
                {
                    if (seenName || !ParseString(entry.name)) return false;
                    seenName = true;
                }
                else if (key == L"icon")
                {
                    if (seenIcon) return false;
                    seenIcon = true;
                    if (MatchLiteral(L"null")) entry.icon.clear();
                    else if (!ParseString(entry.icon)) return false;
                }
                else if (!SkipValue(1))
                    return false;
            }
            while (ConsumeCommaOrEndObject());

            return _closedRoot && Finish() && seenId && seenName && !entry.id.empty() && !entry.name.empty();
        }

        bool ParseCurrentProfileId(std::wstring& currentProfileId)
        {
            currentProfileId.clear();
            bool seen = false;
            if (!BeginRootObject()) return false;

            if (Consume(L'}'))
            {
                return Finish();
            }
            do
            {
                std::wstring key;
                if (!ParseString(key) || !ConsumeColon()) return false;
                if (key == L"currentProfileId")
                {
                    if (seen) return false;
                    seen = true;
                    if (MatchLiteral(L"null")) currentProfileId.clear();
                    else if (!ParseString(currentProfileId)) return false;
                }
                else if (!SkipValue(1))
                    return false;
            }
            while (ConsumeCommaOrEndObject());

            return _closedRoot && Finish();
        }

    private:
        std::wstring_view _input;
        size_t _pos = 0;
        bool _closedRoot = false;

        void SkipWhitespace()
        {
            while (_pos < _input.size() && (_input[_pos] == L' ' || _input[_pos] == L'\t' || _input[_pos] == L'\r' || _input[_pos] == L'\n')) ++_pos;
        }

        bool Finish()
        {
            SkipWhitespace();
            return _pos == _input.size();
        }

        bool BeginRootObject()
        {
            SkipWhitespace();
            return Consume(L'{');
        }

        bool Consume(wchar_t expected)
        {
            SkipWhitespace();
            if (_pos >= _input.size() || _input[_pos] != expected) return false;
            ++_pos;
            return true;
        }

        bool ConsumeColon() { return Consume(L':'); }

        bool ConsumeCommaOrEndObject()
        {
            SkipWhitespace();
            if (_pos < _input.size() && _input[_pos] == L',')
            {
                ++_pos;
                return true;
            }
            if (_pos < _input.size() && _input[_pos] == L'}')
            {
                ++_pos;
                _closedRoot = true;
                return false;
            }
            return false;
        }

        static int HexValue(wchar_t c)
        {
            if (c >= L'0' && c <= L'9')
            {
                return c - L'0';
            }
            if (c >= L'a' && c <= L'f')
            {
                return c - L'a' + 10;
            }
            if (c >= L'A' && c <= L'F')
            {
                return c - L'A' + 10;
            }
            return -1;
        }

        bool ParseHex4(uint16_t& value)
        {
            if (_input.size() - _pos < 4) return false;
            uint16_t result = 0;
            for (int i = 0; i < 4; ++i)
            {
                int nibble = HexValue(_input[_pos++]);
                if (nibble < 0) return false;
                result = static_cast<uint16_t>((result << 4) | nibble);
            }
            value = result;
            return true;
        }

        bool ParseString(std::wstring& value)
        {
            SkipWhitespace();
            if (_pos >= _input.size() || _input[_pos++] != L'"') return false;

            value.clear();
            while (_pos < _input.size())
            {
                wchar_t c = _input[_pos++];
                if (c == L'"')
                {
                    return true;
                }
                if (c < 0x20) return false;
                if (c != L'\\')
                {
                    value.push_back(c);
                    continue;
                }

                if (_pos >= _input.size()) return false;
                wchar_t escape = _input[_pos++];
                switch (escape)
                {
                case L'"': value.push_back(L'"'); break;
                case L'\\': value.push_back(L'\\'); break;
                case L'/': value.push_back(L'/'); break;
                case L'b': value.push_back(L'\b'); break;
                case L'f': value.push_back(L'\f'); break;
                case L'n': value.push_back(L'\n'); break;
                case L'r': value.push_back(L'\r'); break;
                case L't': value.push_back(L'\t'); break;
                case L'u':
                {
                    uint16_t first;
                    if (!ParseHex4(first)) return false;
                    if (first >= 0xD800 && first <= 0xDBFF)
                    {
                        if (_input.size() - _pos < 6 || _input[_pos] != L'\\' || _input[_pos + 1] != L'u') return false;
                        _pos += 2;
                        uint16_t second;
                        if (!ParseHex4(second) || second < 0xDC00 || second > 0xDFFF) return false;
                        value.push_back(static_cast<wchar_t>(first));
                        value.push_back(static_cast<wchar_t>(second));
                    }
                    else if (first >= 0xDC00 && first <= 0xDFFF)
                        return false;
                    else
                        value.push_back(static_cast<wchar_t>(first));
                    break;
                }
                default:
                    return false;
                }
            }

            return false;
        }

        bool MatchLiteral(std::wstring_view literal)
        {
            SkipWhitespace();
            if (_input.substr(_pos, literal.size()) != literal) return false;
            _pos += literal.size();
            return true;
        }

        bool SkipNumber()
        {
            SkipWhitespace();
            size_t start = _pos;
            if (_pos < _input.size() && _input[_pos] == L'-') ++_pos;
            if (_pos >= _input.size()) return false;

            if (_input[_pos] == L'0') ++_pos;
            else if (_input[_pos] >= L'1' && _input[_pos] <= L'9')
            {
                while (_pos < _input.size() && iswdigit(_input[_pos])) ++_pos;
            }
            else return false;

            if (_pos < _input.size() && _input[_pos] == L'.')
            {
                ++_pos;
                size_t digits = _pos;
                while (_pos < _input.size() && iswdigit(_input[_pos])) ++_pos;
                if (_pos == digits) return false;
            }

            if (_pos < _input.size() && (_input[_pos] == L'e' || _input[_pos] == L'E'))
            {
                ++_pos;
                if (_pos < _input.size() && (_input[_pos] == L'+' || _input[_pos] == L'-')) ++_pos;
                size_t digits = _pos;
                while (_pos < _input.size() && iswdigit(_input[_pos])) ++_pos;
                if (_pos == digits) return false;
            }

            return _pos > start;
        }

        bool SkipObject(unsigned depth)
        {
            if (depth > MaxJsonDepth || !Consume(L'{')) return false;
            SkipWhitespace();
            if (Consume(L'}'))
            {
                return true;
            }

            while (true)
            {
                std::wstring key;
                if (!ParseString(key) || !ConsumeColon() || !SkipValue(depth + 1)) return false;
                SkipWhitespace();
                if (Consume(L'}'))
                {
                    return true;
                }
                if (!Consume(L',')) return false;
            }
        }

        bool SkipArray(unsigned depth)
        {
            if (depth > MaxJsonDepth || !Consume(L'[')) return false;
            SkipWhitespace();
            if (Consume(L']'))
            {
                return true;
            }

            while (true)
            {
                if (!SkipValue(depth + 1)) return false;
                SkipWhitespace();
                if (Consume(L']'))
                {
                    return true;
                }
                if (!Consume(L',')) return false;
            }
        }

        bool SkipValue(unsigned depth)
        {
            if (depth > MaxJsonDepth) return false;
            SkipWhitespace();
            if (_pos >= _input.size()) return false;

            wchar_t c = _input[_pos];
            if (c == L'"')
            {
                std::wstring ignored;
                return ParseString(ignored);
            }
            if (c == L'{')
            {
                return SkipObject(depth);
            }
            if (c == L'[')
            {
                return SkipArray(depth);
            }
            if (c == L't')
            {
                return MatchLiteral(L"true");
            }
            if (c == L'f')
            {
                return MatchLiteral(L"false");
            }
            if (c == L'n')
            {
                return MatchLiteral(L"null");
            }
            return SkipNumber();
        }
    };

    bool ParseProfileJson(std::wstring_view json, ProfileEntry& entry) { return JsonParser(json).ParseProfile(entry); }
    bool ParseCurrentProfileIdJson(std::wstring_view json, std::wstring& currentProfileId) { return JsonParser(json).ParseCurrentProfileId(currentProfileId); }
}

std::wstring ReadCurrentProfileId(const std::wstring& settingsPath)
{
    std::wstring json;
    size_t bytesRead = 0;
    if (!ReadFileUtf8(settingsPath, MaxJsonFileBytes, json, bytesRead)) return {};

    std::wstring currentProfileId;
    return ParseCurrentProfileIdJson(json, currentProfileId) ? currentProfileId : std::wstring{};
}

std::vector<ProfileEntry> ReadProfiles(const std::wstring& profilesDir)
{
    std::vector<ProfileEntry> profiles;
    size_t aggregateBytes = 0;
    size_t filesConsidered = 0;

    std::wstring pattern = profilesDir + L"\\*.dpm";
    WIN32_FIND_DATAW fd{};
    HANDLE hFind = FindFirstFileW(pattern.c_str(), &fd);
    if (hFind == INVALID_HANDLE_VALUE)
        return profiles;

    do
    {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
            continue;

        if (filesConsidered >= MaxProfileFiles)
            break;

        unsigned long long fileBytes = (static_cast<unsigned long long>(fd.nFileSizeHigh) << 32) | fd.nFileSizeLow;
        if (fileBytes == 0)
        {
            ++filesConsidered;
            continue;
        }
        if (fileBytes > MaxJsonFileBytes)
        {
            ++filesConsidered;
            continue;
        }
        if (!ProfileBudgetAllows(static_cast<size_t>(fileBytes), aggregateBytes, filesConsidered))
            break;

        std::wstring filePath = profilesDir + L"\\" + fd.cFileName;
        std::wstring json;
        size_t bytesRead = 0;
        ++filesConsidered;
        if (!ReadFileUtf8(filePath, MaxJsonFileBytes, json, bytesRead))
            continue;
        aggregateBytes += bytesRead;

        ProfileEntry entry;
        if (!ParseProfileJson(json, entry))
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

#ifdef DPM_SHELLEXT_TESTS
bool ParseProfileJsonForTest(std::wstring_view json, ProfileEntry& entry) { return ParseProfileJson(json, entry); }
bool ParseCurrentProfileIdJsonForTest(std::wstring_view json, std::wstring& currentProfileId) { return ParseCurrentProfileIdJson(json, currentProfileId); }
bool DecodeUtf8ForTest(const std::string& bytes, std::wstring& decoded) { return DecodeUtf8(bytes, decoded); }
bool ProfileBudgetAllowsForTest(size_t fileBytes, size_t aggregateBytes, size_t filesConsidered) { return ProfileBudgetAllows(fileBytes, aggregateBytes, filesConsidered); }
#endif