#include "../DisplayProfileManager.SetupBootstrap/BootstrapPolicy.h"

#include <iostream>
#include <string>
#include <vector>

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

    int Count(const std::vector<std::wstring>& args, const wchar_t* value)
    {
        int count = 0;
        for (const auto& arg : args)
        {
            if (arg == value)
                ++count;
        }
        return count;
    }
}

int wmain()
{
    auto none = BuildChoiceLabels({});
    Check(none.currentUser == L"Current User (recommended)" && none.allUsers == L"All Users", "neither labels");

    auto current = BuildChoiceLabels({ true, false });
    Check(current.currentUser == L"Current User (installed)" && current.allUsers == L"All Users", "current-only labels");
    Check(current.currentUser.find(L"recommended") == std::wstring::npos && current.allUsers.find(L"recommended") == std::wstring::npos, "current-only no recommendation");

    auto all = BuildChoiceLabels({ false, true });
    Check(all.currentUser == L"Current User" && all.allUsers == L"All Users (installed)", "all-only labels");
    Check(all.currentUser.find(L"recommended") == std::wstring::npos && all.allUsers.find(L"recommended") == std::wstring::npos, "all-only no recommendation");

    auto both = BuildChoiceLabels({ true, true });
    Check(both.currentUser == L"Current User (installed)" && both.allUsers == L"All Users (installed)", "both labels");
    Check(both.currentUser.find(L"recommended") == std::wstring::npos && both.allUsers.find(L"recommended") == std::wstring::npos, "both no recommendation");

    Check(GetDefaultChoice({}) == DefaultChoice::CurrentUser, "fresh install defaults current user");
    Check(GetDefaultChoice({ true, false }) == DefaultChoice::Cancel, "current-only defaults cancel");
    Check(GetDefaultChoice({ false, true }) == DefaultChoice::Cancel, "all-only defaults cancel");
    Check(GetDefaultChoice({ true, true }) == DefaultChoice::Cancel, "both defaults cancel");

    auto explicitCurrent = EvaluateArguments({ L"/CURRENTUSER", L"/SKIPDOTNETBOOTSTRAP", L"/LOG=some path\\setup.log" });
    Check(explicitCurrent.valid && !explicitCurrent.showChooser && explicitCurrent.explicitScope == SetupScope::CurrentUser, "explicit current bypasses chooser");
    Check(Count(explicitCurrent.forwarded, L"/CURRENTUSER") == 1 && Count(explicitCurrent.forwarded, L"/SKIPDOTNETBOOTSTRAP") == 1, "explicit current args preserved once");

    auto explicitAll = EvaluateArguments({ L"/allusers" });
    Check(explicitAll.valid && !explicitAll.showChooser && explicitAll.explicitScope == SetupScope::AllUsers, "explicit all bypasses chooser");
    Check(Count(explicitAll.forwarded, L"/ALLUSERS") == 1, "explicit all canonicalized once");

    auto repeatedCurrent = EvaluateArguments({ L"/CURRENTUSER", L"/currentuser", L"/LOG=test" });
    Check(repeatedCurrent.valid && Count(repeatedCurrent.forwarded, L"/CURRENTUSER") == 1, "repeated current canonicalized once");
    Check(Count(repeatedCurrent.forwarded, L"/LOG=test") == 1, "repeated current preserves other args");

    Check(!EvaluateArguments({ L"/CURRENTUSER", L"/ALLUSERS" }).valid, "both scopes rejected");
    Check(!EvaluateArguments({ L"/VERYSILENT" }).valid, "silent without scope rejected");
    Check(EvaluateArguments({ L"/SILENT", L"/CURRENTUSER" }).valid, "silent explicit scope allowed");

    Check(DecideBootstrapRoute(SetupScope::CurrentUser, false, false) == BootstrapRoute::RunPayload, "current medium runs payload");
    Check(DecideBootstrapRoute(SetupScope::AllUsers, false, false) == BootstrapRoute::SelfElevate, "all medium self elevates");
    Check(DecideBootstrapRoute(SetupScope::AllUsers, true, false) == BootstrapRoute::RunPayload, "all high runs payload");
    Check(DecideBootstrapRoute(SetupScope::AllUsers, false, true) == BootstrapRoute::RejectElevationRequired, "internal token medium rejected");
    Check(DecideBootstrapRoute(SetupScope::AllUsers, true, true) == BootstrapRoute::RunPayload, "internal token high all runs payload");
    Check(DecideBootstrapRoute(SetupScope::CurrentUser, true, true) == BootstrapRoute::RejectInvalid, "internal token current rejected");

    auto internalAll = EvaluateArguments({ L"--dpm-bootstrap-elevated-allusers", L"/ALLUSERS", L"/SKIPDOTNETBOOTSTRAP", L"/LOG=a b" });
    Check(internalAll.valid && internalAll.internalElevatedAllUsers && internalAll.explicitScope == SetupScope::AllUsers, "internal all route parsed");
    Check(Count(internalAll.forwarded, L"/ALLUSERS") == 1, "internal all scope once");
    Check(Count(internalAll.forwarded, L"/SKIPDOTNETBOOTSTRAP") == 1, "internal skip dotnet once");
    Check(Count(internalAll.forwarded, L"--dpm-bootstrap-elevated-allusers") == 0, "internal token stripped from payload");
    Check(!EvaluateArguments({ L"--dpm-bootstrap-elevated-allusers", L"/CURRENTUSER" }).valid, "internal current rejected");

    auto interactive = EvaluateArguments({ L"/SKIPDOTNETBOOTSTRAP", L"/LOG=alpha beta" });
    Check(interactive.valid && interactive.showChooser, "interactive chooser");
    auto selectedCurrent = ApplySelectedScope(interactive, SetupScope::CurrentUser);
    auto selectedAll = ApplySelectedScope(interactive, SetupScope::AllUsers);
    Check(Count(selectedCurrent, L"/CURRENTUSER") == 1 && Count(selectedCurrent, L"/ALLUSERS") == 0, "selected current exactly once");
    Check(Count(selectedAll, L"/ALLUSERS") == 1 && Count(selectedAll, L"/CURRENTUSER") == 0, "selected all exactly once");
    Check(Count(selectedCurrent, L"/SKIPDOTNETBOOTSTRAP") == 1, "skip dotnet survives");
    Check(Count(selectedCurrent, L"/LOG=alpha beta") == 1, "arbitrary args survive once");

    Check(QuoteWindowsArgument(L"plain") == L"plain", "plain quote");
    Check(QuoteWindowsArgument(L"two words") == L"\"two words\"", "space quote");
    Check(QuoteWindowsArgument(LR"(a\"b c)") == LR"EXPECTED("a\\\"b c")EXPECTED", "embedded quote");
    Check(QuoteWindowsArgument(LR"(C:\path with space\)") == LR"("C:\path with space\\")", "trailing slash quote");

    const auto command = BuildChildCommandLine(LR"(C:\Temp Dir\payload.exe)", { LR"(/LOG=C:\A B\log.txt)", L"/CURRENTUSER" });
    Check(command == LR"("C:\Temp Dir\payload.exe" "/LOG=C:\A B\log.txt" /CURRENTUSER)", "child command line");

    const auto elevateArgs = BuildElevationArguments({ LR"(/LOG=C:\A B\log.txt)", L"/SKIPDOTNETBOOTSTRAP", L"/ALLUSERS" });
    Check(Count(elevateArgs, L"--dpm-bootstrap-elevated-allusers") == 1, "elevation token once");
    Check(Count(elevateArgs, L"/SKIPDOTNETBOOTSTRAP") == 1, "elevation skip dotnet once");
    Check(Count(elevateArgs, L"/ALLUSERS") == 1, "elevation allusers once");
    const auto elevationTail = BuildArgumentTail({ LR"(C:\path with space\)", LR"(a\"b c)", L"/ALLUSERS" });
    Check(elevationTail == LR"("C:\path with space\\" "a\\\"b c" /ALLUSERS)", "elevation argument tail quoting");

    if (failures == 0)
        std::cout << "Setup bootstrap policy tests PASS\n";
    return failures == 0 ? 0 : 1;
}
