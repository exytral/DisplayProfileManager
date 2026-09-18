#pragma once
#include <cstddef>
#include <string_view>
#include <string>
#include <vector>

struct ProfileEntry
{
    std::wstring id;
    std::wstring name;
    std::wstring icon; // Bare filename; empty means no custom icon
};

std::wstring ReadCurrentProfileId(const std::wstring& settingsPath);
std::vector<ProfileEntry> ReadProfiles(const std::wstring& profilesDir);

inline constexpr size_t MaxJsonFileBytes = 10u * 1024u * 1024u;
inline constexpr size_t MaxProfileAggregateBytes = 32u * 1024u * 1024u;
inline constexpr size_t MaxProfileFiles = 4096u;
inline constexpr unsigned MaxJsonDepth = 64u;

#ifdef DPM_SHELLEXT_TESTS
bool ParseProfileJsonForTest(std::wstring_view json, ProfileEntry& entry);
bool ParseCurrentProfileIdJsonForTest(std::wstring_view json, std::wstring& currentProfileId);
bool DecodeUtf8ForTest(const std::string& bytes, std::wstring& decoded);
bool ProfileBudgetAllowsForTest(size_t fileBytes, size_t aggregateBytes, size_t filesConsidered);
#endif