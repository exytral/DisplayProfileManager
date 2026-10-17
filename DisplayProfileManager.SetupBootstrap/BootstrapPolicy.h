#pragma once

#include <string>
#include <vector>

enum class SetupScope
{
    None,
    CurrentUser,
    AllUsers,
};

enum class DefaultChoice
{
    CurrentUser,
    Cancel,
};

enum class BootstrapRoute
{
    RejectInvalid,
    RejectElevationRequired,
    RunPayload,
    SelfElevate,
};

struct InstallState
{
    bool currentUser = false;
    bool allUsers = false;
};

struct ChoiceLabels
{
    std::wstring currentUser;
    std::wstring allUsers;
};

struct ArgumentPolicy
{
    bool valid = true;
    bool silent = false;
    bool showChooser = false;
    bool internalElevatedAllUsers = false;
    SetupScope explicitScope = SetupScope::None;
    std::vector<std::wstring> forwarded;
};

ChoiceLabels BuildChoiceLabels(InstallState state);
DefaultChoice GetDefaultChoice(InstallState state);
ArgumentPolicy EvaluateArguments(const std::vector<std::wstring>& arguments);
std::vector<std::wstring> ApplySelectedScope(const ArgumentPolicy& policy, SetupScope scope);
BootstrapRoute DecideBootstrapRoute(SetupScope scope, bool processElevated, bool internalElevatedAllUsers);
std::vector<std::wstring> BuildElevationArguments(const std::vector<std::wstring>& publicArguments);
std::wstring QuoteWindowsArgument(const std::wstring& argument);
std::wstring BuildArgumentTail(const std::vector<std::wstring>& arguments);
std::wstring BuildChildCommandLine(const std::wstring& executable, const std::vector<std::wstring>& arguments);
