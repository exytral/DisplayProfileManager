#include "../DisplayProfileManager.ShellExt/JsonReader.h"
#include <iostream>
#include <string>

namespace
{
    int failures = 0;

    void Check(bool condition, const char* name)
    {
        if (!condition)
        {
            std::cerr << "FAIL: " << name << '\n';
            ++failures;
        }
    }

    bool Parse(std::wstring_view json, ProfileEntry& entry) { return ParseProfileJsonForTest(json, entry); }
}

int wmain()
{
    ProfileEntry entry;

    Check(Parse(LR"({"id":"1","name":"ASCII","icon":"icon.ico"})", entry) && entry.name == L"ASCII", "ASCII profile");
    Check(Parse(LR"({"id":"1","name":"q\" b\\ s\/ \b\f\n\r\t","icon":null})", entry) && entry.name == L"q\" b\\ s/ \b\f\n\r\t", "JSON escapes");
    Check(Parse(LR"({"id":"1","name":"\u03A9","icon":null})", entry) && entry.name == L"\u03A9", "BMP unicode escape");
    Check(Parse(LR"({"id":"1","name":"\uD83D\uDE00","icon":null})", entry) && entry.name.size() == 2 && entry.name[0] == 0xD83D && entry.name[1] == 0xDE00, "surrogate pair");
    Check(Parse(LR"({"meta":{"name":"decoy"},"id":"1","name":"root","unknown":[1,true,false,null,{"x":2}],"icon":null})", entry) && entry.name == L"root", "root key scope and arbitrary skip");

    Check(!Parse(L"{\"id\":\"1\",\"name\":\"truncated", entry), "truncated string rejected");
    Check(!Parse(LR"({"id":"1","name":"bad\x","icon":null})", entry), "invalid escape rejected");
    Check(!Parse(LR"({"id":"1","name":"\u12","icon":null})", entry), "short unicode escape rejected");
    Check(!Parse(LR"({"id":"1","name":"\uD83Dx","icon":null})", entry), "invalid surrogate pairing rejected");
    Check(!Parse(LR"([{"id":"1","name":"array"}])", entry), "non-object root rejected");
    Check(!Parse(std::wstring(L"{\"id\":\"1\",\"name\":\"bad") + wchar_t(1) + L"\",\"icon\":null}", entry), "raw C0 rejected");
    Check(!Parse(LR"({"id":"1","id":"2","name":"duplicate","icon":null})", entry), "duplicate id rejected");
    Check(!Parse(LR"({"id":"1","name":"duplicate","name":"again","icon":null})", entry), "duplicate name rejected");
    Check(!Parse(LR"({"id":"1","name":"duplicate","icon":null,"icon":"x"})", entry), "duplicate icon rejected");

    std::wstring decoded;
    Check(!DecodeUtf8ForTest(std::string("\xC3\x28", 2), decoded), "malformed UTF-8 rejected");

    Check(ProfileBudgetAllowsForTest(MaxJsonFileBytes, 0, 0), "per-file boundary accepted");
    Check(!ProfileBudgetAllowsForTest(0, 0, 0), "empty file is not parse work");
    Check(!ProfileBudgetAllowsForTest(MaxJsonFileBytes + 1, 0, 0), "per-file overflow rejected");
    Check(ProfileBudgetAllowsForTest(1, MaxProfileAggregateBytes - 1, 0), "aggregate boundary accepted");
    Check(!ProfileBudgetAllowsForTest(2, MaxProfileAggregateBytes - 1, 0), "aggregate overflow rejected");
    Check(ProfileBudgetAllowsForTest(1, 0, MaxProfileFiles - 1), "file-count boundary accepted");
    Check(!ProfileBudgetAllowsForTest(1, 0, MaxProfileFiles), "file-count overflow rejected");

    std::wstring current;
    Check(ParseCurrentProfileIdJsonForTest(LR"({"nested":{"currentProfileId":"decoy"},"currentProfileId":"root"})", current) && current == L"root", "settings root key scope");
    Check(!ParseCurrentProfileIdJsonForTest(LR"({"currentProfileId":"one","currentProfileId":"two"})", current), "duplicate settings key rejected");

    if (failures == 0)
        std::cout << "JsonReader tests passed\n";
    return failures == 0 ? 0 : 1;
}
