#pragma once
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