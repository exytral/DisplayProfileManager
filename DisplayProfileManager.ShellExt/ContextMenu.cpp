#include "ContextMenu.h"
#include "resource.h"
#include <shellapi.h>
#include <shlwapi.h>

#pragma comment(lib, "shlwapi.lib")

extern HINSTANCE g_hInst;
extern LONG      g_cDllRef;

DpmContextMenu::DpmContextMenu()
    : _refCount(1), _idCmdFirst(0)
{
    InterlockedIncrement(&g_cDllRef);
}

DpmContextMenu::~DpmContextMenu()
{
    InterlockedDecrement(&g_cDllRef);
}

STDMETHODIMP DpmContextMenu::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;

    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IContextMenu))
        *ppv = static_cast<IContextMenu*>(this);
    else if (IsEqualIID(riid, IID_IShellExtInit))
        *ppv = static_cast<IShellExtInit*>(this);
    else
        return E_NOINTERFACE;

    AddRef();
    return S_OK;
}

STDMETHODIMP_(ULONG) DpmContextMenu::AddRef()
{
    return InterlockedIncrement(&_refCount);
}

STDMETHODIMP_(ULONG) DpmContextMenu::Release()
{
    LONG ref = InterlockedDecrement(&_refCount);
    if (ref == 0) delete this;
    return ref;
}

STDMETHODIMP DpmContextMenu::Initialize(PCIDLIST_ABSOLUTE pidlFolder,
    IDataObject* /*pdobj*/,
    HKEY /*hkeyProgID*/)
{
    // Limit extension to desktop namespace rather than folder views
    if (!IsDesktopFolder(pidlFolder))
        return E_FAIL;

    std::wstring appDataDir = ResolveAppDataDir();
    if (appDataDir.empty())
        return E_FAIL;

    _exePath = ResolveExePath();
    _iconsDir = appDataDir + L"\\Icons";

    std::wstring settingsPath = appDataDir + L"\\Settings.json";
    std::wstring profilesDir = appDataDir + L"\\Profiles";

    _currentProfileId = ReadCurrentProfileId(settingsPath);
    _profiles = ReadProfiles(profilesDir);

    return S_OK;
}

STDMETHODIMP DpmContextMenu::QueryContextMenu(HMENU hmenu, UINT indexMenu,
    UINT idCmdFirst, UINT idCmdLast,
    UINT uFlags)
{
    if (uFlags & CMF_DEFAULTONLY)
        return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, 0);

    if (_profiles.empty())
        return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, 0);

    _idCmdFirst = idCmdFirst;

    HMENU hSub = CreatePopupMenu();
    if (!hSub)
        return E_OUTOFMEMORY;

    int iconSize = GetSystemMetrics(SM_CXSMICON); // Match shell's menu icon size

    for (UINT i = 0; i < static_cast<UINT>(_profiles.size()); ++i)
    {
        const auto& p = _profiles[i];
        bool isActive = (!_currentProfileId.empty() && p.id == _currentProfileId);

        MENUITEMINFOW mii{};
        mii.cbSize = sizeof(mii);
        mii.fMask = MIIM_ID | MIIM_STRING | MIIM_STATE | MIIM_FTYPE | MIIM_BITMAP;
        mii.wID = idCmdFirst + i;
        mii.dwTypeData = const_cast<LPWSTR>(p.name.c_str());
        mii.fType = MFT_STRING;
        mii.fState = MFS_ENABLED;

        if (isActive)
        {
            // Active item uses radio marker in place of its profile icon
            mii.fType |= MFT_RADIOCHECK;
            mii.fState |= MFS_CHECKED;
        }

        // Radio marker and menu bitmap share same gutter, so active items omit bitmap
        HBITMAP hBmp = nullptr;
        if (!isActive && !p.icon.empty())
            hBmp = LoadProfileIconBitmap(p.icon, iconSize);

        mii.hbmpItem = hBmp; // nullptr leaves item without a profile icon

        InsertMenuItemW(hSub, i, TRUE, &mii);
    }

    MENUITEMINFOW rootMii{};
    rootMii.cbSize = sizeof(rootMii);
    rootMii.fMask = MIIM_STRING | MIIM_STATE | MIIM_SUBMENU | MIIM_BITMAP;
    rootMii.fState = MFS_ENABLED;
    rootMii.hSubMenu = hSub;
    rootMii.dwTypeData = const_cast<LPWSTR>(L"Display Profiles");
    rootMii.hbmpItem = LoadAppIconBitmap(iconSize);

    InsertMenuItemW(hmenu, indexMenu, TRUE, &rootMii);

    return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, static_cast<UINT>(_profiles.size()));
}

STDMETHODIMP DpmContextMenu::InvokeCommand(CMINVOKECOMMANDINFO* pici)
{
    if (HIWORD(pici->lpVerb) != 0)
        return E_INVALIDARG;

    UINT idx = LOWORD(pici->lpVerb);
    if (idx >= static_cast<UINT>(_profiles.size()))
        return E_INVALIDARG;

    if (_exePath.empty())
        return E_FAIL;

    const auto& profile = _profiles[idx];

    // Escape quotes so profile name remains one command-line argument
    std::wstring safeName = profile.name;
    size_t pos = 0;
    while ((pos = safeName.find(L'"', pos)) != std::wstring::npos)
    {
        safeName.replace(pos, 1, L"\\\"");
        pos += 2;
    }

    std::wstring args = L"--headless \"" + safeName + L"\"";

    SHELLEXECUTEINFOW sei{};
    sei.cbSize = sizeof(sei);
    sei.fMask = SEE_MASK_NOCLOSEPROCESS;
    sei.hwnd = pici->hwnd;
    sei.lpVerb = L"open";
    sei.lpFile = _exePath.c_str();
    sei.lpParameters = args.c_str();
    sei.nShow = SW_HIDE;

    ShellExecuteExW(&sei);
    if (sei.hProcess)
        CloseHandle(sei.hProcess);

    return S_OK;
}

STDMETHODIMP DpmContextMenu::GetCommandString(UINT_PTR idCmd, UINT uType,
    UINT* /*pReserved*/,
    CHAR* pszName, UINT cchMax)
{
    if (idCmd >= static_cast<UINT_PTR>(_profiles.size()))
        return E_INVALIDARG;

    if (uType == GCS_VERBW)
    {
        std::wstring verb = L"dpm_profile_" + std::to_wstring(idCmd);
        wcsncpy_s(reinterpret_cast<WCHAR*>(pszName), cchMax, verb.c_str(), _TRUNCATE);
        return S_OK;
    }

    return E_NOTIMPL;
}

bool DpmContextMenu::IsDesktopFolder(PCIDLIST_ABSOLUTE pidlFolder)
{
    if (!pidlFolder)
        return false;

    PIDLIST_ABSOLUTE pidlDesktop = nullptr;
    if (FAILED(SHGetSpecialFolderLocation(nullptr, CSIDL_DESKTOP, &pidlDesktop)))
        return false;

    bool isDesktop = ILIsEqual(pidlFolder, pidlDesktop) == TRUE;
    ILFree(pidlDesktop);
    return isDesktop;
}

HBITMAP DpmContextMenu::LoadAppIconBitmap(int size)
{
    HICON hIcon = static_cast<HICON>(
        LoadImageW(g_hInst, MAKEINTRESOURCEW(IDI_APPICON),
            IMAGE_ICON, size, size, LR_DEFAULTCOLOR));
    if (!hIcon) return nullptr;

    HBITMAP hBmp = IconToBitmap(hIcon, size);
    DestroyIcon(hIcon);
    return hBmp;
}

HBITMAP DpmContextMenu::LoadProfileIconBitmap(const std::wstring& filename, int size)
{
    // Reject separators and parent traversal before appending filename to sandbox path
    if (filename.find(L'/') != std::wstring::npos ||
        filename.find(L'\\') != std::wstring::npos ||
        filename.find(L"..") != std::wstring::npos)
        return nullptr;

    std::wstring path = _iconsDir + L"\\" + filename;

    HICON hIcon = static_cast<HICON>(
        LoadImageW(nullptr, path.c_str(), IMAGE_ICON, size, size,
            LR_LOADFROMFILE | LR_DEFAULTCOLOR));
    if (!hIcon) return nullptr;

    HBITMAP hBmp = IconToBitmap(hIcon, size);
    DestroyIcon(hIcon);
    return hBmp;
}

HBITMAP DpmContextMenu::IconToBitmap(HICON hIcon, int size)
{
    HDC hdcScreen = GetDC(nullptr);
    HDC hdcMem = CreateCompatibleDC(hdcScreen);

    BITMAPINFOHEADER bih{};
    bih.biSize = sizeof(bih);
    bih.biWidth = size;
    bih.biHeight = -size; // Top-down keeps DIB origin aligned with menu drawing
    bih.biPlanes = 1;
    bih.biBitCount = 32;
    bih.biCompression = BI_RGB;

    void* pvBits = nullptr;
    HBITMAP hBmp = CreateDIBSection(hdcScreen, reinterpret_cast<BITMAPINFO*>(&bih),
        DIB_RGB_COLORS, &pvBits, nullptr, 0);
    if (hBmp)
    {
        HGDIOBJ hOld = SelectObject(hdcMem, hBmp);
        RECT rc{ 0, 0, size, size };
        FillRect(hdcMem, &rc, static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));
        DrawIconEx(hdcMem, 0, 0, hIcon, size, size, 0, nullptr, DI_NORMAL);

        // Premultiply alpha channel required by 32bpp menu bitmaps
        auto* pixels = static_cast<DWORD*>(pvBits);
        int count = size * size;
        for (int i = 0; i < count; ++i)
        {
            DWORD px = pixels[i];
            BYTE  a = (px >> 24) & 0xFF;
            BYTE  r = static_cast<BYTE>(((px >> 16) & 0xFF) * a / 255);
            BYTE  g = static_cast<BYTE>(((px >> 8) & 0xFF) * a / 255);
            BYTE  b = static_cast<BYTE>((px & 0xFF) * a / 255);
            pixels[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }

        SelectObject(hdcMem, hOld);
    }

    DeleteDC(hdcMem);
    ReleaseDC(nullptr, hdcScreen);
    return hBmp;
}

std::wstring DpmContextMenu::ResolveExePath()
{
    // Executable is deployed beside shell extension DLL
    wchar_t dllPath[MAX_PATH]{};
    GetModuleFileNameW(g_hInst, dllPath, MAX_PATH);
    PathRemoveFileSpecW(dllPath);
    std::wstring dir = dllPath;
    return dir + L"\\DisplayProfileManager.exe";
}

std::wstring DpmContextMenu::ResolveAppDataDir()
{
    wchar_t appData[MAX_PATH]{};
    if (FAILED(SHGetFolderPathW(nullptr, CSIDL_APPDATA, nullptr, SHGFP_TYPE_CURRENT, appData)))
        return {};
    return std::wstring(appData) + L"\\DisplayProfileManager";
}

STDMETHODIMP DpmClassFactory::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory))
    {
        *ppv = static_cast<IClassFactory*>(this);
        AddRef();
        return S_OK;
    }
    return E_NOINTERFACE;
}

STDMETHODIMP_(ULONG) DpmClassFactory::AddRef()
{
    return InterlockedIncrement(&_refCount);
}

STDMETHODIMP_(ULONG) DpmClassFactory::Release()
{
    LONG ref = InterlockedDecrement(&_refCount);
    if (ref == 0) delete this;
    return ref;
}

STDMETHODIMP DpmClassFactory::CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (pUnkOuter) return CLASS_E_NOAGGREGATION;

    auto* obj = new (std::nothrow) DpmContextMenu();
    if (!obj) return E_OUTOFMEMORY;

    HRESULT hr = obj->QueryInterface(riid, ppv);
    obj->Release();
    return hr;
}

STDMETHODIMP DpmClassFactory::LockServer(BOOL fLock)
{
    if (fLock) InterlockedIncrement(&g_cDllRef);
    else        InterlockedDecrement(&g_cDllRef);
    return S_OK;
}