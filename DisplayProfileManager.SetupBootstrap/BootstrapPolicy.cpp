#include "BootstrapPolicy.h"

#include <cwctype>

namespace
{
    constexpr wchar_t InternalElevationToken[] = L"--dpm-bootstrap-elevated-allusers";

    bool EqualsInsensitive(const std::wstring& left, const wchar_t* right)
    {
        std::wstring rhs(right);
        if (left.size() != rhs.size())
        {
            return false;
        }

        for (size_t i = 0; i < left.size(); ++i)
        {
            if (std::towupper(left[i]) != std::towupper(rhs[i]))
            {
                return false;
            }
        }

        return true;
    }
}

ChoiceLabels BuildChoiceLabels(InstallState state)
{
    ChoiceLabels labels;

    if (state.currentUser)
        labels.currentUser = L"Current User (installed)";
    else if (!state.allUsers)
        labels.currentUser = L"Current User (recommended)";
    else
        labels.currentUser = L"Current User";

    labels.allUsers = state.allUsers ? L"All Users (installed)" : L"All Users";
    return labels;
}

DefaultChoice GetDefaultChoice(InstallState state)
{
    return (!state.currentUser && !state.allUsers)
        ? DefaultChoice::CurrentUser
        : DefaultChoice::Cancel;
}

ArgumentPolicy EvaluateArguments(const std::vector<std::wstring>& arguments)
{
    ArgumentPolicy policy;
    bool current = false;
    bool all = false;

    for (const auto& argument : arguments)
    {
        if (EqualsInsensitive(argument, L"/CURRENTUSER"))
        {
            current = true;
            continue;
        }

        if (EqualsInsensitive(argument, L"/ALLUSERS"))
        {
            all = true;
            continue;
        }

        if (EqualsInsensitive(argument, InternalElevationToken))
        {
            policy.internalElevatedAllUsers = true;
            continue;
        }

        if (EqualsInsensitive(argument, L"/SILENT") || EqualsInsensitive(argument, L"/VERYSILENT"))
            policy.silent = true;

        policy.forwarded.push_back(argument);
    }

    if (current && all)
    {
        policy.valid = false;
        return policy;
    }

    if (current)
        policy.explicitScope = SetupScope::CurrentUser;
    else if (all)
        policy.explicitScope = SetupScope::AllUsers;

    if (policy.internalElevatedAllUsers && policy.explicitScope != SetupScope::AllUsers)
    {
        policy.valid = false;
        return policy;
    }

    if (policy.explicitScope != SetupScope::None)
    {
        policy.forwarded = ApplySelectedScope(policy, policy.explicitScope);
        return policy;
    }

    if (policy.silent || policy.internalElevatedAllUsers)
    {
        policy.valid = false;
        return policy;
    }

    policy.showChooser = true;
    return policy;
}

std::vector<std::wstring> ApplySelectedScope(const ArgumentPolicy& policy, SetupScope scope)
{
    std::vector<std::wstring> result = policy.forwarded;
    if (scope == SetupScope::CurrentUser)
        result.emplace_back(L"/CURRENTUSER");
    else if (scope == SetupScope::AllUsers)
        result.emplace_back(L"/ALLUSERS");
    return result;
}

BootstrapRoute DecideBootstrapRoute(SetupScope scope, bool processElevated, bool internalElevatedAllUsers)
{
    if (scope == SetupScope::None)
    {
        return BootstrapRoute::RejectInvalid;
    }

    if (internalElevatedAllUsers)
    {
        if (scope != SetupScope::AllUsers)
        {
            return BootstrapRoute::RejectInvalid;
        }
        return processElevated ? BootstrapRoute::RunPayload : BootstrapRoute::RejectElevationRequired;
    }

    if (scope == SetupScope::CurrentUser)
    {
        return BootstrapRoute::RunPayload;
    }

    return processElevated ? BootstrapRoute::RunPayload : BootstrapRoute::SelfElevate;
}

std::vector<std::wstring> BuildElevationArguments(const std::vector<std::wstring>& publicArguments)
{
    std::vector<std::wstring> result;
    result.reserve(publicArguments.size() + 1);
    result.emplace_back(InternalElevationToken);
    result.insert(result.end(), publicArguments.begin(), publicArguments.end());
    return result;
}

std::wstring QuoteWindowsArgument(const std::wstring& argument)
{
    if (argument.empty())
    {
        return L"\"\"";
    }

    if (argument.find_first_of(L" \t\"") == std::wstring::npos)
    {
        return argument;
    }

    std::wstring quoted = L"\"";
    size_t slashes = 0;

    for (wchar_t ch : argument)
    {
        if (ch == L'\\')
        {
            ++slashes;
            continue;
        }

        if (ch == L'\"')
        {
            quoted.append(slashes * 2 + 1, L'\\');
            quoted.push_back(L'\"');
            slashes = 0;
            continue;
        }

        quoted.append(slashes, L'\\');
        slashes = 0;
        quoted.push_back(ch);
    }

    quoted.append(slashes * 2, L'\\');
    quoted.push_back(L'\"');
    return quoted;
}

std::wstring BuildArgumentTail(const std::vector<std::wstring>& arguments)
{
    std::wstring result;
    for (const auto& argument : arguments)
    {
        if (!result.empty())
            result.push_back(L' ');
        result += QuoteWindowsArgument(argument);
    }
    return result;
}

std::wstring BuildChildCommandLine(const std::wstring& executable, const std::vector<std::wstring>& arguments)
{
    std::wstring commandLine = QuoteWindowsArgument(executable);
    if (!arguments.empty())
    {
        commandLine.push_back(L' ');
        commandLine += BuildArgumentTail(arguments);
    }
    return commandLine;
}
