#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <string>
#include <vector>
#include "JsonReader.h"

static constexpr CLSID CLSID_DpmContextMenu =
{ 0x58c9dbb4, 0x174a, 0x4bca, { 0x88, 0xed, 0x54, 0xd7, 0x60, 0x32, 0x34, 0x00 } };

class DpmContextMenu : public IShellExtInit, public IContextMenu
{
public:
    DpmContextMenu();
    virtual ~DpmContextMenu();

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    STDMETHODIMP Initialize(PCIDLIST_ABSOLUTE pidlFolder, IDataObject* pdobj, HKEY hkeyProgID) override;

    STDMETHODIMP QueryContextMenu(HMENU hmenu, UINT indexMenu, UINT idCmdFirst,
                                  UINT idCmdLast, UINT uFlags) override;
    STDMETHODIMP InvokeCommand(CMINVOKECOMMANDINFO* pici) override;
    STDMETHODIMP GetCommandString(UINT_PTR idCmd, UINT uType,
                                  UINT* pReserved, CHAR* pszName, UINT cchMax) override;

private:
    LONG  _refCount;
    UINT  _idCmdFirst;

    std::vector<ProfileEntry> _profiles;
    std::wstring              _currentProfileId;
    std::wstring              _exePath;   // Resolved path to DisplayProfileManager.exe
    std::wstring              _iconsDir;  // Profile icon sandbox under AppData

    bool IsDesktopFolder(PCIDLIST_ABSOLUTE pidlFolder);
    HBITMAP LoadAppIconBitmap(int size);
    HBITMAP LoadProfileIconBitmap(const std::wstring& filename, int size);
    HBITMAP IconToBitmap(HICON hIcon, int size);
    std::wstring ResolveExePath();
    std::wstring ResolveAppDataDir();
};

class DpmClassFactory : public IClassFactory
{
public:
    DpmClassFactory() : _refCount(1) {}

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;
    STDMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv) override;
    STDMETHODIMP LockServer(BOOL fLock) override;

private:
    LONG _refCount;
};