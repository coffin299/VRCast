// VRCast のアップデーター（VRCastUpdater.exe）。
// VRCast 本体が新しい版をダウンロード・検証・展開したあとに起動し、本体の終了を待ってからインストール先のファイルを差し替え、
// VRCast を起動し直す。途中で失敗したら元のファイルに戻す。
// 他のアプリ（OBS・Discord 等）が読み込み中の仮想カメラの DLL は削除・上書きできないが名前の変更はできるため、
// 旧ファイルは同じドライブの退避フォルダへ移動してから新しいファイルを置く（中身が同じファイルは触らない）。
//
// 使い方（VRCast が渡す。直接起動したときは説明を出して終了する）:
//   VRCastUpdater.exe --pid <VRCast のプロセス ID> --source <展開した新しい VRCast フォルダ>
//                     --target <インストール先> --exe <起動し直す exe 名> --log <ログ> --result <結果ファイル>

#include <windows.h>
#include <shellapi.h>
#include <tlhelp32.h>

#include <cstdio>
#include <cstring>
#include <cwchar>
#include <set>
#include <string>
#include <vector>

namespace
{
    // 退避フォルダ名（インストール先の直下。VRCast が次の起動時に削除する）
    constexpr wchar_t BackupFolderName[] = L".vrcast-update-backup";

    // VRCast とその子プロセスの終了を待つ最長時間（ミリ秒）と、確認の間隔
    constexpr DWORD ExitTimeout = 60000;
    constexpr DWORD PollInterval = 500;

    // ファイル操作のやり直し（ウイルス対策ソフトの検査などで一時的に開けないことがある）
    constexpr int RetryCount = 20;
    constexpr DWORD RetryInterval = 250;

    // 中身の比較の読み取り単位（バイト）
    constexpr DWORD CompareBufferSize = 1 << 20;

    // 実行ファイルのパスの最大長（文字。長いパスにも対応）
    constexpr DWORD MaxPathLength = 32768;

    // 起動引数
    struct Options
    {
        DWORD pid = 0;
        std::wstring source;
        std::wstring target;
        std::wstring exe;
        std::wstring log;
        std::wstring result;
    };

    // 差し替えで動かしたファイル（インストール先からの相対パス）。失敗時に元へ戻すために記録する
    struct Change
    {
        std::vector<std::wstring> moved;
        std::vector<std::wstring> copied;
    };

    FILE* g_log = nullptr;

    std::string ToUtf8(const std::wstring& text)
    {
        // 空文字は変換しない（WideCharToMultiByte は長さ 0 を失敗として扱う）
        if (text.empty())
        {
            return std::string();
        }

        int size = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), int(text.size()), nullptr, 0, nullptr, nullptr);
        std::string utf8(size_t(size), '\0');
        WideCharToMultiByte(CP_UTF8, 0, text.c_str(), int(text.size()), utf8.data(), size, nullptr, nullptr);
        return utf8;
    }

    void Log(const std::wstring& message)
    {
        // ログを開けなかったときは書かない（更新自体は続ける）
        if (g_log == nullptr)
        {
            return;
        }

        // 「時刻 内容」の 1 行を UTF-8 で追記し、途中で落ちても残るようすぐに書き出す
        SYSTEMTIME now;
        GetLocalTime(&now);
        wchar_t stamp[32];
        swprintf_s(stamp, L"%04u-%02u-%02u %02u:%02u:%02u ", now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute,
            now.wSecond);
        std::string line = ToUtf8(stamp + message) + "\r\n";
        fwrite(line.data(), 1, line.size(), g_log);
        fflush(g_log);
    }

    std::wstring ErrorText(DWORD code)
    {
        // 利用者に見せる理由は Windows のエラー番号（説明文は環境の言語で長くなるため付けない）
        return L"Windows error " + std::to_wstring(code);
    }

    std::wstring Long(const std::wstring& path)
    {
        // 260 文字を超えるパス（トラッカーの深いフォルダ等）も扱えるよう \\?\ を付ける（ネットワークは \\?\UNC\）
        if (path.rfind(L"\\\\?\\", 0) == 0)
        {
            return path;
        }

        if (path.rfind(L"\\\\", 0) == 0)
        {
            return L"\\\\?\\UNC\\" + path.substr(2);
        }

        return L"\\\\?\\" + path;
    }

    std::wstring FullPath(const std::wstring& path)
    {
        // \\?\ を付けると "." や "/" が解釈されなくなるため、先に絶対パスへ正規化する
        DWORD size = GetFullPathNameW(path.c_str(), 0, nullptr, nullptr);
        if (size == 0)
        {
            return std::wstring();
        }

        std::wstring full(size, L'\0');
        DWORD length = GetFullPathNameW(path.c_str(), size, full.data(), nullptr);
        full.resize(length);

        // 末尾の区切りを除く（ドライブ直下 "C:\" はそのまま）
        while (full.size() > 3 && (full.back() == L'\\' || full.back() == L'/'))
        {
            full.pop_back();
        }

        return full;
    }

    std::wstring Join(const std::wstring& folder, const std::wstring& name)
    {
        return folder + L"\\" + name;
    }

    std::wstring ParentOf(const std::wstring& path)
    {
        // 最後の区切りより前（区切りが無ければ空）
        size_t separator = path.find_last_of(L'\\');
        return separator == std::wstring::npos ? std::wstring() : path.substr(0, separator);
    }

    std::wstring Lower(const std::wstring& text)
    {
        // Windows のファイル名は大文字小文字を区別しないため、比較用に小文字へそろえる
        std::wstring lower = text;
        if (!lower.empty())
        {
            CharLowerBuffW(lower.data(), DWORD(lower.size()));
        }

        return lower;
    }

    bool IsFile(const std::wstring& path)
    {
        DWORD attributes = GetFileAttributesW(Long(path).c_str());
        return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
    }

    bool IsDirectory(const std::wstring& path)
    {
        DWORD attributes = GetFileAttributesW(Long(path).c_str());
        return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    }

    bool IsUnder(const std::wstring& path, const std::wstring& folder)
    {
        // folder の中（folder 自身は含まない）にあれば true。大文字小文字は区別しない
        return path.size() > folder.size() && path[folder.size()] == L'\\'
            && CompareStringOrdinal(path.c_str(), int(folder.size()), folder.c_str(), int(folder.size()), TRUE) == CSTR_EQUAL;
    }

    void CreateDirectories(const std::wstring& path)
    {
        // 上の階層から順に作る（既にある・作れない階層の失敗は無視し、後のファイル操作の失敗で検出する）
        for (size_t separator = path.find(L'\\', 3); separator != std::wstring::npos;
             separator = path.find(L'\\', separator + 1))
        {
            CreateDirectoryW(Long(path.substr(0, separator)).c_str(), nullptr);
        }

        CreateDirectoryW(Long(path).c_str(), nullptr);
    }

    template <typename Action>
    bool Retry(Action action)
    {
        for (int i = 0; i < RetryCount; i++)
        {
            if (action())
            {
                return true;
            }

            // 使用中・アクセス拒否以外（存在しない等）は待っても変わらないのでやり直さない
            DWORD error = GetLastError();
            if (error != ERROR_SHARING_VIOLATION && error != ERROR_ACCESS_DENIED && error != ERROR_LOCK_VIOLATION)
            {
                SetLastError(error);
                return false;
            }

            Sleep(RetryInterval);
            SetLastError(error);
        }

        return false;
    }

    void ListFiles(const std::wstring& root, const std::wstring& relative, std::vector<std::wstring>& files)
    {
        // root からの相対パスでファイルを集める（フォルダはたどり、ジャンクション等のリンク先はたどらない）
        std::wstring folder = relative.empty() ? root : Join(root, relative);
        WIN32_FIND_DATAW data;
        HANDLE find = FindFirstFileW(Long(Join(folder, L"*")).c_str(), &data);
        if (find == INVALID_HANDLE_VALUE)
        {
            return;
        }

        do
        {
            std::wstring name = data.cFileName;
            if (name == L"." || name == L"..")
            {
                continue;
            }

            std::wstring child = relative.empty() ? name : Join(relative, name);
            if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
            {
                files.push_back(child);
            }
            else if ((data.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0)
            {
                ListFiles(root, child, files);
            }
        } while (FindNextFileW(find, &data));

        FindClose(find);
    }

    std::vector<std::wstring> ListTopLevel(const std::wstring& folder)
    {
        // 直下のファイル・フォルダの名前
        std::vector<std::wstring> names;
        WIN32_FIND_DATAW data;
        HANDLE find = FindFirstFileW(Long(Join(folder, L"*")).c_str(), &data);
        if (find == INVALID_HANDLE_VALUE)
        {
            return names;
        }

        do
        {
            std::wstring name = data.cFileName;
            if (name != L"." && name != L"..")
            {
                names.push_back(name);
            }
        } while (FindNextFileW(find, &data));

        FindClose(find);
        return names;
    }

    void RemoveEmptyDirectories(const std::wstring& folder)
    {
        // 下の階層から空のフォルダを消す（中身が残るフォルダは消えない）
        WIN32_FIND_DATAW data;
        HANDLE find = FindFirstFileW(Long(Join(folder, L"*")).c_str(), &data);
        if (find != INVALID_HANDLE_VALUE)
        {
            do
            {
                std::wstring name = data.cFileName;
                bool directory = (data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                bool link = (data.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
                if (directory && !link && name != L"." && name != L"..")
                {
                    RemoveEmptyDirectories(Join(folder, name));
                }
            } while (FindNextFileW(find, &data));

            FindClose(find);
        }

        RemoveDirectoryW(Long(folder).c_str());
    }

    bool FilesEqual(const std::wstring& first, const std::wstring& second)
    {
        // 大きさが違えば読まずに別物とする
        WIN32_FILE_ATTRIBUTE_DATA a;
        WIN32_FILE_ATTRIBUTE_DATA b;
        if (!GetFileAttributesExW(Long(first).c_str(), GetFileExInfoStandard, &a)
            || !GetFileAttributesExW(Long(second).c_str(), GetFileExInfoStandard, &b)
            || a.nFileSizeHigh != b.nFileSizeHigh || a.nFileSizeLow != b.nFileSizeLow)
        {
            return false;
        }

        // 読み込み中の DLL も読めるよう、他のプロセスの読み書き・削除を妨げずに開く
        const DWORD share = FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE;
        HANDLE fileA = CreateFileW(Long(first).c_str(), GENERIC_READ, share, nullptr, OPEN_EXISTING,
            FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
        HANDLE fileB = CreateFileW(Long(second).c_str(), GENERIC_READ, share, nullptr, OPEN_EXISTING,
            FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
        bool equal = fileA != INVALID_HANDLE_VALUE && fileB != INVALID_HANDLE_VALUE;

        // 先頭から同じ大きさずつ読み、違う箇所があれば別物
        std::vector<char> bufferA(equal ? CompareBufferSize : 0);
        std::vector<char> bufferB(equal ? CompareBufferSize : 0);
        while (equal)
        {
            DWORD readA = 0;
            DWORD readB = 0;
            if (!ReadFile(fileA, bufferA.data(), CompareBufferSize, &readA, nullptr)
                || !ReadFile(fileB, bufferB.data(), CompareBufferSize, &readB, nullptr) || readA != readB)
            {
                equal = false;
                break;
            }

            // 両方とも最後まで読んだ
            if (readA == 0)
            {
                break;
            }

            equal = std::memcmp(bufferA.data(), bufferB.data(), readA) == 0;
        }

        if (fileA != INVALID_HANDLE_VALUE)
        {
            CloseHandle(fileA);
        }

        if (fileB != INVALID_HANDLE_VALUE)
        {
            CloseHandle(fileB);
        }

        return equal;
    }

    std::wstring FindRunningIn(const std::wstring& folder)
    {
        // folder の中の実行ファイルで動いているプロセス（VRCast・トラッカー・クラッシュハンドラー）を 1 つ探す
        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE)
        {
            return std::wstring();
        }

        std::wstring found;
        std::vector<wchar_t> path(MaxPathLength);
        PROCESSENTRY32W entry = {};
        entry.dwSize = sizeof(entry);
        DWORD self = GetCurrentProcessId();
        for (BOOL ok = Process32FirstW(snapshot, &entry); ok && found.empty(); ok = Process32NextW(snapshot, &entry))
        {
            // 自分自身とシステムのプロセスは対象外
            if (entry.th32ProcessID == self || entry.th32ProcessID == 0)
            {
                continue;
            }

            // 別ユーザー・保護されたプロセスは開けない（VRCast の子ではないので無視してよい）
            HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, entry.th32ProcessID);
            if (process == nullptr)
            {
                continue;
            }

            DWORD size = MaxPathLength;
            if (QueryFullProcessImageNameW(process, 0, path.data(), &size) && IsUnder(std::wstring(path.data(), size), folder))
            {
                found.assign(path.data(), size);
            }

            CloseHandle(process);
        }

        CloseHandle(snapshot);
        return found;
    }

    bool WaitForExit(const Options& options)
    {
        // VRCast 本体の終了を待つ（既に終了していれば開けないのでそのまま進む）
        HANDLE process = OpenProcess(SYNCHRONIZE, FALSE, options.pid);
        if (process != nullptr)
        {
            DWORD wait = WaitForSingleObject(process, ExitTimeout);
            CloseHandle(process);
            if (wait != WAIT_OBJECT_0)
            {
                Log(L"VRCast (pid " + std::to_wstring(options.pid) + L") did not exit");
                return false;
            }
        }

        // インストール先の実行ファイルで動くもの（トラッカー・クラッシュハンドラー）も終わるまで待つ
        ULONGLONG deadline = GetTickCount64() + ExitTimeout;
        for (;;)
        {
            std::wstring running = FindRunningIn(options.target);
            if (running.empty())
            {
                return true;
            }

            if (GetTickCount64() >= deadline)
            {
                Log(L"Still running: " + running);
                return false;
            }

            Sleep(PollInterval);
        }
    }

    bool Validate(const Options& options, std::wstring& error)
    {
        // 起動し直す exe 名はファイル名だけ（別の場所を指せないようにする）
        if (options.exe.find_first_of(L"\\/:") != std::wstring::npos || options.exe.find(L"..") != std::wstring::npos
            || options.exe.size() < 5 || Lower(options.exe.substr(options.exe.size() - 4)) != L".exe")
        {
            error = L"Invalid executable name";
            return false;
        }

        // 取り違え防止: どちらも VRCast のフォルダ（exe と _Data がある）で、互いの中に無いこと
        std::wstring data = options.exe.substr(0, options.exe.size() - 4) + L"_Data";
        if (!IsFile(Join(options.target, options.exe)) || !IsFile(Join(options.source, options.exe))
            || !IsDirectory(Join(options.source, data)))
        {
            error = L"The update files are incomplete";
            return false;
        }

        if (Lower(options.source) == Lower(options.target) || IsUnder(options.source, options.target)
            || IsUnder(options.target, options.source))
        {
            error = L"Invalid folders";
            return false;
        }

        return true;
    }

    bool Apply(const Options& options, const std::wstring& backup, Change& change, std::wstring& error)
    {
        // 新しい版のファイル（相対パス）と、差し替える範囲（新しい版の直下にある名前。ユーザーが置いた他のものは触らない）
        std::vector<std::wstring> sourceFiles;
        ListFiles(options.source, std::wstring(), sourceFiles);
        std::set<std::wstring> sourceSet;
        for (const std::wstring& file : sourceFiles)
        {
            sourceSet.insert(Lower(file));
        }

        std::vector<std::wstring> roots = ListTopLevel(options.source);

        // 差し替える範囲にある今のファイル（フォルダは中身ごと。新しい版に無いファイルは退避されて消える）
        std::vector<std::wstring> targetFiles;
        for (const std::wstring& root : roots)
        {
            std::wstring path = Join(options.target, root);
            if (IsDirectory(path))
            {
                ListFiles(options.target, root, targetFiles);
            }
            else if (IsFile(path))
            {
                targetFiles.push_back(root);
            }
        }

        Log(L"Files: " + std::to_wstring(sourceFiles.size()) + L" new, " + std::to_wstring(targetFiles.size()) + L" current");

        // 中身が同じファイルはそのまま残し、それ以外は退避する（読み込み中の DLL も名前の変更はできる）
        std::set<std::wstring> kept;
        for (const std::wstring& file : targetFiles)
        {
            std::wstring lower = Lower(file);
            if (sourceSet.count(lower) != 0 && FilesEqual(Join(options.source, file), Join(options.target, file)))
            {
                kept.insert(lower);
                continue;
            }

            std::wstring from = Join(options.target, file);
            std::wstring to = Join(backup, file);
            CreateDirectories(ParentOf(to));
            if (!Retry([&] { return MoveFileExW(Long(from).c_str(), Long(to).c_str(), 0) != FALSE; }))
            {
                error = L"Could not move " + file + L" (" + ErrorText(GetLastError()) + L")";
                return false;
            }

            change.moved.push_back(file);
        }

        Log(L"Unchanged: " + std::to_wstring(kept.size()) + L", moved: " + std::to_wstring(change.moved.size()));

        // 中身が無くなったフォルダを消す（新しい版に無いフォルダを残さない。必要なものは次のコピーで作り直す）
        for (const std::wstring& root : roots)
        {
            if (IsDirectory(Join(options.target, root)))
            {
                RemoveEmptyDirectories(Join(options.target, root));
            }
        }

        // 新しい版のファイルを置く（退避済みの場所なので上書きは起きない。残っていれば失敗として扱う）
        for (const std::wstring& file : sourceFiles)
        {
            if (kept.count(Lower(file)) != 0)
            {
                continue;
            }

            std::wstring from = Join(options.source, file);
            std::wstring to = Join(options.target, file);
            CreateDirectories(ParentOf(to));
            if (!Retry([&] { return CopyFileW(Long(from).c_str(), Long(to).c_str(), TRUE) != FALSE; }))
            {
                error = L"Could not copy " + file + L" (" + ErrorText(GetLastError()) + L")";
                return false;
            }

            change.copied.push_back(file);
        }

        Log(L"Copied: " + std::to_wstring(change.copied.size()));
        return true;
    }

    void Rollback(const Options& options, const std::wstring& backup, const Change& change)
    {
        // 置いた新しいファイルを消す（後から置いたものから）
        for (auto file = change.copied.rbegin(); file != change.copied.rend(); ++file)
        {
            std::wstring path = Join(options.target, *file);
            if (!Retry([&] { return DeleteFileW(Long(path).c_str()) != FALSE; }))
            {
                Log(L"Rollback: could not delete " + *file + L" (" + ErrorText(GetLastError()) + L")");
            }
        }

        // 退避した元のファイルを戻す（消したフォルダは作り直す）
        for (auto file = change.moved.rbegin(); file != change.moved.rend(); ++file)
        {
            std::wstring from = Join(backup, *file);
            std::wstring to = Join(options.target, *file);
            CreateDirectories(ParentOf(to));
            if (!Retry([&] { return MoveFileExW(Long(from).c_str(), Long(to).c_str(), MOVEFILE_REPLACE_EXISTING) != FALSE; }))
            {
                Log(L"Rollback: could not restore " + *file + L" (" + ErrorText(GetLastError()) + L")");
            }
        }

        Log(L"Rolled back");
    }

    void WriteResult(const std::wstring& path, bool ok, const std::wstring& error)
    {
        // 1 行目に ok / failed、2 行目に理由（VRCast が次の起動時に読んで表示する）
        FILE* file = nullptr;
        if (_wfopen_s(&file, path.c_str(), L"wb") != 0 || file == nullptr)
        {
            Log(L"Could not write the result file");
            return;
        }

        std::string text = ok ? "ok\n" : "failed\n" + ToUtf8(error) + "\n";
        fwrite(text.data(), 1, text.size(), file);
        fclose(file);
    }

    void Launch(const Options& options)
    {
        // インストール先を作業フォルダにして VRCast を起動する（この更新プログラムと同じ権限）
        std::wstring exe = Join(options.target, options.exe);
        std::wstring commandLine = L"\"" + exe + L"\"";
        STARTUPINFOW startup = {};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION information = {};
        if (!CreateProcessW(exe.c_str(), commandLine.data(), nullptr, nullptr, FALSE, 0, nullptr, options.target.c_str(),
                &startup, &information))
        {
            Log(L"Could not start VRCast (" + ErrorText(GetLastError()) + L")");
            return;
        }

        CloseHandle(information.hThread);
        CloseHandle(information.hProcess);
        Log(L"Started " + exe);
    }

    bool Parse(Options& options)
    {
        // "--名前 値" の組を読む（知らない名前・値の欠けは不正）
        int count = 0;
        LPWSTR* args = CommandLineToArgvW(GetCommandLineW(), &count);
        if (args == nullptr)
        {
            return false;
        }

        bool valid = count > 1 && (count - 1) % 2 == 0;
        for (int i = 1; valid && i + 1 < count; i += 2)
        {
            std::wstring name = args[i];
            std::wstring value = args[i + 1];
            if (name == L"--pid")
            {
                options.pid = DWORD(wcstoul(value.c_str(), nullptr, 10));
            }
            else if (name == L"--source")
            {
                options.source = FullPath(value);
            }
            else if (name == L"--target")
            {
                options.target = FullPath(value);
            }
            else if (name == L"--exe")
            {
                options.exe = value;
            }
            else if (name == L"--log")
            {
                options.log = FullPath(value);
            }
            else if (name == L"--result")
            {
                options.result = FullPath(value);
            }
            else
            {
                valid = false;
            }
        }

        LocalFree(args);
        return valid && options.pid != 0 && !options.source.empty() && !options.target.empty() && !options.exe.empty()
            && !options.log.empty() && !options.result.empty();
    }

    std::wstring BackupFolder(const std::wstring& target)
    {
        // インストール先の直下（同じドライブなので名前の変更で移動できる）に、更新ごとの日時のフォルダを作る
        std::wstring root = Join(target, BackupFolderName);
        SYSTEMTIME now;
        GetLocalTime(&now);
        wchar_t name[32];
        swprintf_s(name, L"%04u%02u%02u-%02u%02u%02u", now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute,
            now.wSecond);
        CreateDirectories(Join(root, name));

        // エクスプローラーで目立たないよう隠しフォルダにする
        SetFileAttributesW(Long(root).c_str(), FILE_ATTRIBUTE_HIDDEN);
        return Join(root, name);
    }
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    // VRCast 以外から起動されたら説明だけ出して終了する
    Options options;
    if (!Parse(options))
    {
        MessageBoxW(nullptr,
            L"VRCast が更新に使うプログラムです。VRCast.exe を起動してください。\n\n"
            L"This program is used by VRCast to install updates. Start VRCast.exe instead.",
            L"VRCast Updater", MB_OK | MB_ICONINFORMATION);
        return 2;
    }

    if (_wfopen_s(&g_log, options.log.c_str(), L"ab") != 0)
    {
        g_log = nullptr;
    }

    Log(L"Updating " + options.target + L" from " + options.source);

    // VRCast が終わらなければ何も変えない（動いている VRCast は起動し直さない）
    bool exited = WaitForExit(options);
    bool ok = false;
    std::wstring error;
    if (!exited)
    {
        error = L"VRCast did not exit";
    }
    else if (Validate(options, error))
    {
        // 差し替え、失敗したら元に戻す
        std::wstring backup = BackupFolder(options.target);
        Change change;
        ok = Apply(options, backup, change, error);
        if (!ok)
        {
            Rollback(options, backup, change);
        }
    }

    // 結果を残して VRCast を起動し直す（失敗時は元の版が起動して理由を表示する）
    WriteResult(options.result, ok, error);
    if (exited)
    {
        Launch(options);
    }

    Log(ok ? std::wstring(L"Done") : L"Failed: " + error);
    if (g_log != nullptr)
    {
        fclose(g_log);
    }

    return ok ? 0 : 1;
}
