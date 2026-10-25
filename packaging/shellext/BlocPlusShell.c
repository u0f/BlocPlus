// "Edit with BlocPlus" in the Windows 11 Explorer context menu, for the Microsoft Store package.
// That menu only shows IExplorerCommand handlers, so this tiny COM server is declared in
// AppxManifest.xml and loaded by Explorer. pack.ps1 builds it with LLVM-MinGW and generates
// titles.h (one translation per UI language) from lang\*.txt.
#define COBJMACROS
#define WIN32_LEAN_AND_MEAN
#define _WIN32_WINNT 0x0A00 // Windows 10, for IApplicationActivationManager
#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <appmodel.h>
#include "titles.h" // static const wchar_t *Titles[][2] = { { L"en", L"Edit with BlocPlus" }, ... };

// {88C865C7-50FF-4056-A6D7-B9076DBDC537}, also in AppxManifest.xml
static const CLSID CLSID_EditWith = { 0x88c865c7, 0x50ff, 0x4056, { 0xa6, 0xd7, 0xb9, 0x07, 0x6d, 0xbd, 0xc5, 0x37 } };
static wchar_t dir[MAX_PATH]; // the package folder, where this DLL and BlocPlus.exe live
static LONG objects;

static const wchar_t *Find(const wchar_t *lang)
{
    for (int i = 0; i < ARRAYSIZE(Titles); i++)
        if (!lstrcmpiW(Titles[i][0], lang)) return Titles[i][1];
    return NULL;
}

// The Windows display language, falling back like the app does: zh-TW -> zh-Hant, pt-BR, es-MX -> es, else English.
static const wchar_t *Title(void)
{
    wchar_t lang[LOCALE_NAME_MAX_LENGTH];
    const wchar_t *t;
    if (!LCIDToLocaleName(MAKELCID(GetUserDefaultUILanguage(), SORT_DEFAULT), lang, ARRAYSIZE(lang), 0)) return Find(L"en");
    if ((t = Find(lang))) return t;
    if (!StrCmpNIW(lang, L"zh-", 3))
        return Find(!lstrcmpiW(lang, L"zh-TW") || !lstrcmpiW(lang, L"zh-HK") || !lstrcmpiW(lang, L"zh-MO") ? L"zh-Hant" : L"zh-Hans");
    wchar_t *dash = StrChrW(lang, L'-');
    if (dash) *dash = 0;
    return (t = Find(lang)) ? t : Find(L"en");
}

// App user model ID of this package's app: its family name (from AppxManifest.xml next to the DLL) + "!BlocPlus".
static BOOL Aumid(wchar_t *out, UINT32 size)
{
    wchar_t path[MAX_PATH], name[256], publisher[512];
    char buf[8192];
    DWORD read = 0;
    lstrcpyW(path, dir);
    PathAppendW(path, L"AppxManifest.xml");
    HANDLE f = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return FALSE;
    ReadFile(f, buf, sizeof buf - 1, &read, NULL);
    CloseHandle(f);
    buf[read] = 0;
    char *id = StrStrA(buf, "<Identity "), *n, *p, *e;
    if (!id || !(n = StrStrA(id, " Name=\"")) || !(p = StrStrA(id, " Publisher=\""))) return FALSE;
    n += 7; p += 12;
    if (!(e = StrChrA(n, '"'))) return FALSE;
    name[MultiByteToWideChar(CP_UTF8, 0, n, (int)(e - n), name, ARRAYSIZE(name) - 1)] = 0;
    if (!(e = StrChrA(p, '"'))) return FALSE;
    publisher[MultiByteToWideChar(CP_UTF8, 0, p, (int)(e - p), publisher, ARRAYSIZE(publisher) - 1)] = 0;
    PACKAGE_ID pid = { 0 };
    pid.name = name;
    pid.publisher = publisher;
    UINT32 len = size - 10;
    if (PackageFamilyNameFromId(&pid, &len, out) != ERROR_SUCCESS) return FALSE;
    lstrcatW(out, L"!BlocPlus");
    return TRUE;
}

typedef struct { IExplorerCommand cmd; LONG refs; } Command;

static HRESULT STDMETHODCALLTYPE QueryInterface(IExplorerCommand *This, REFIID riid, void **ppv)
{
    if (IsEqualIID(riid, &IID_IUnknown) || IsEqualIID(riid, &IID_IExplorerCommand))
    {
        *ppv = This;
        IExplorerCommand_AddRef(This);
        return S_OK;
    }
    *ppv = NULL;
    return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE AddRef(IExplorerCommand *This) { return InterlockedIncrement(&((Command *)This)->refs); }

static ULONG STDMETHODCALLTYPE Release(IExplorerCommand *This)
{
    LONG n = InterlockedDecrement(&((Command *)This)->refs);
    if (!n) { HeapFree(GetProcessHeap(), 0, This); InterlockedDecrement(&objects); }
    return n;
}

static HRESULT STDMETHODCALLTYPE GetTitle(IExplorerCommand *This, IShellItemArray *items, LPWSTR *name) { return SHStrDupW(Title(), name); }

static HRESULT STDMETHODCALLTYPE GetIcon(IExplorerCommand *This, IShellItemArray *items, LPWSTR *icon)
{
    wchar_t path[MAX_PATH + 4];
    lstrcpyW(path, dir);
    PathAppendW(path, L"BlocPlus.exe");
    lstrcatW(path, L",0");
    return SHStrDupW(path, icon);
}

static HRESULT STDMETHODCALLTYPE GetToolTip(IExplorerCommand *This, IShellItemArray *items, LPWSTR *tip) { *tip = NULL; return E_NOTIMPL; }
static HRESULT STDMETHODCALLTYPE GetCanonicalName(IExplorerCommand *This, GUID *guid) { *guid = CLSID_EditWith; return S_OK; }
static HRESULT STDMETHODCALLTYPE GetState(IExplorerCommand *This, IShellItemArray *items, BOOL slow, EXPCMDSTATE *state) { *state = ECS_ENABLED; return S_OK; }
static HRESULT STDMETHODCALLTYPE GetFlags(IExplorerCommand *This, EXPCMDFLAGS *flags) { *flags = ECF_DEFAULT; return S_OK; }
static HRESULT STDMETHODCALLTYPE EnumSubCommands(IExplorerCommand *This, IEnumExplorerCommand **e) { *e = NULL; return E_NOTIMPL; }

// Starts the packaged app with the selected files as arguments, each one quoted.
static HRESULT STDMETHODCALLTYPE Invoke(IExplorerCommand *This, IShellItemArray *items, IBindCtx *ctx)
{
    wchar_t aumid[512];
    DWORD count = 0;
    if (!items || !Aumid(aumid, ARRAYSIZE(aumid))) return E_FAIL;
    IShellItemArray_GetCount(items, &count);
    size_t cap = 1024, len = 0;
    wchar_t *args = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, cap * sizeof(wchar_t));
    for (DWORD i = 0; i < count && args; i++)
    {
        IShellItem *item;
        LPWSTR path;
        if (FAILED(IShellItemArray_GetItemAt(items, i, &item))) continue;
        if (SUCCEEDED(IShellItem_GetDisplayName(item, SIGDN_FILESYSPATH, &path)))
        {
            size_t need = len + lstrlenW(path) + 4;
            if (need > cap)
            {
                wchar_t *bigger = HeapReAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, args, (cap = need * 2) * sizeof(wchar_t));
                if (!bigger) { CoTaskMemFree(path); IShellItem_Release(item); break; }
                args = bigger;
            }
            if (len) args[len++] = L' ';
            args[len++] = L'"';
            lstrcpyW(args + len, path);
            len += lstrlenW(path);
            args[len++] = L'"';
            args[len] = 0;
            CoTaskMemFree(path);
        }
        IShellItem_Release(item);
    }
    IApplicationActivationManager *aam;
    HRESULT hr = CoCreateInstance(&CLSID_ApplicationActivationManager, NULL, CLSCTX_LOCAL_SERVER, &IID_IApplicationActivationManager, (void **)&aam);
    if (SUCCEEDED(hr))
    {
        DWORD pid;
        hr = IApplicationActivationManager_ActivateApplication(aam, aumid, args ? args : L"", AO_NONE, &pid);
        IApplicationActivationManager_Release(aam);
    }
    HeapFree(GetProcessHeap(), 0, args);
    return hr;
}

static IExplorerCommandVtbl CommandVtbl = { QueryInterface, AddRef, Release, GetTitle, GetIcon, GetToolTip,
    GetCanonicalName, GetState, Invoke, GetFlags, EnumSubCommands };

static HRESULT STDMETHODCALLTYPE FactoryQueryInterface(IClassFactory *This, REFIID riid, void **ppv)
{
    if (IsEqualIID(riid, &IID_IUnknown) || IsEqualIID(riid, &IID_IClassFactory)) { *ppv = This; return S_OK; }
    *ppv = NULL;
    return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE FactoryAddRef(IClassFactory *This) { return 2; }  // static object
static ULONG STDMETHODCALLTYPE FactoryRelease(IClassFactory *This) { return 1; }

static HRESULT STDMETHODCALLTYPE CreateInstance(IClassFactory *This, IUnknown *outer, REFIID riid, void **ppv)
{
    *ppv = NULL;
    if (outer) return CLASS_E_NOAGGREGATION;
    Command *c = HeapAlloc(GetProcessHeap(), 0, sizeof *c);
    if (!c) return E_OUTOFMEMORY;
    c->cmd.lpVtbl = &CommandVtbl;
    c->refs = 1;
    InterlockedIncrement(&objects);
    HRESULT hr = IExplorerCommand_QueryInterface(&c->cmd, riid, ppv);
    IExplorerCommand_Release(&c->cmd);
    return hr;
}

static HRESULT STDMETHODCALLTYPE LockServer(IClassFactory *This, BOOL lock)
{
    if (lock) InterlockedIncrement(&objects); else InterlockedDecrement(&objects);
    return S_OK;
}

static IClassFactoryVtbl FactoryVtbl = { FactoryQueryInterface, FactoryAddRef, FactoryRelease, CreateInstance, LockServer };
static IClassFactory Factory = { &FactoryVtbl };

// Exported through BlocPlusShell.def.
HRESULT WINAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void **ppv)
{
    if (!IsEqualCLSID(clsid, &CLSID_EditWith)) { *ppv = NULL; return CLASS_E_CLASSNOTAVAILABLE; }
    return IClassFactory_QueryInterface(&Factory, riid, ppv);
}

HRESULT WINAPI DllCanUnloadNow(void) { return objects ? S_FALSE : S_OK; }

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void *reserved)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(instance);
        GetModuleFileNameW(instance, dir, ARRAYSIZE(dir));
        PathRemoveFileSpecW(dir);
    }
    return TRUE;
}
