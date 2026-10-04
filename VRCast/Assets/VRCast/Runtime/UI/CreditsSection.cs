using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VRCast.UI
{
    /// <summary>
    /// Credits タブ（開発者・協力者、ライセンスと NOTICE の本文）。
    /// 本文は配布フォルダの LICENSE.txt / NOTICE.txt（エディターではリポジトリ直下の LICENSE / NOTICE）から読み、
    /// 見つからないときは GitHub のファイルを開くボタンにする。
    /// </summary>
    public class CreditsSection
    {
        // GitHub 上のライセンス・NOTICE
        private const string RepositoryUrl = "https://github.com/coffin299/VRCast/blob/main/";

        // 名前ボタンの幅
        private const float NameButtonWidth = 160f;

        // 開発者・協力者（役割の 5 言語表記、名前、リンク先）
        private static readonly Person[] People =
        {
            new Person(new Role("Developer", "開発者", "개발자", "开发者", "開發者"),
                "ごみぃ", "https://x.com/coffin299"),
            new Person(new Role("Collaborator", "協力者", "협력자", "协力者", "協力者"),
                "Arche_039", "https://x.com/Arche_039"),
        };

        // 読み込んだ本文（段落ごと。IMGUI の 1 ラベルの頂点数上限を超えないよう分ける）。null = 未読込、空 = 見つからない
        private List<string> _license;
        private List<string> _notice;

        // 役割の表記（Loc.T と同じ並び: 英語・日本語・韓国語・簡体字・繁体字）
        private readonly struct Role
        {
            public readonly string English;
            public readonly string Japanese;
            public readonly string Korean;
            public readonly string ChineseSimplified;
            public readonly string ChineseTraditional;

            public Role(string english, string japanese, string korean, string chineseSimplified,
                string chineseTraditional)
            {
                English = english;
                Japanese = japanese;
                Korean = korean;
                ChineseSimplified = chineseSimplified;
                ChineseTraditional = chineseTraditional;
            }

            // 表示言語に合わせた表記
            public string Text => Loc.T(English, Japanese, Korean, ChineseSimplified, ChineseTraditional);
        }

        private readonly struct Person
        {
            public readonly Role Role;
            public readonly string Name;
            public readonly string Url;

            public Person(Role role, string name, string url)
            {
                Role = role;
                Name = name;
                Url = url;
            }
        }

        public void Draw()
        {
            DrawPeople();

            // 本文は初回表示時に一度だけ読む
            _license ??= LoadParagraphs("LICENSE");
            _notice ??= LoadParagraphs("NOTICE");
            DrawDocument(Loc.T("License", "ライセンス", "라이선스", "许可证", "授權條款"), _license, "LICENSE");
            DrawDocument("NOTICE", _notice, "NOTICE");
        }

        private static void DrawPeople()
        {
            GuiControls.BeginCard(Loc.T("Credits", "クレジット", "크레딧", "致谢", "致謝"));

            // 役割と名前（押すとリンク先を開く）
            foreach (Person person in People)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(person.Role.Text, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(person.Name, GUILayout.Width(NameButtonWidth)))
                {
                    Application.OpenURL(person.Url);
                }

                GUILayout.EndHorizontal();
            }

            GuiControls.EndCard();
        }

        private static void DrawDocument(string title, List<string> paragraphs, string fileName)
        {
            GuiControls.BeginCard(title);

            // 見つからなければ GitHub のファイルを開くボタンだけ
            if (paragraphs.Count == 0)
            {
                GuiControls.Hint(Loc.T("The file was not found next to the app.", "アプリのフォルダにファイルが見つかりません。",
                    "앱 폴더에서 파일을 찾을 수 없습니다.", "在应用文件夹中找不到该文件。", "在應用程式資料夾中找不到該檔案。"));
                if (GUILayout.Button(Loc.T("Open on GitHub", "GitHub で開く", "GitHub에서 열기",
                        "在 GitHub 上打开", "在 GitHub 上開啟")))
                {
                    Application.OpenURL(RepositoryUrl + fileName);
                }

                GuiControls.EndCard();
                return;
            }

            // 本文を段落ごとに表示
            foreach (string paragraph in paragraphs)
            {
                GuiControls.Hint(paragraph);
            }

            GuiControls.EndCard();
        }

        private static List<string> LoadParagraphs(string baseName)
        {
            var paragraphs = new List<string>();
            string path = FindDocument(baseName);
            if (path == null)
            {
                return paragraphs;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 読めないときは見つからない扱い（GitHub のボタンを出す）
                return paragraphs;
            }

            // 空行で段落に分け、前後の空白は落とす
            string[] blocks = text.Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string block in blocks)
            {
                string trimmed = block.Trim('\n');
                if (trimmed.Trim().Length > 0)
                {
                    paragraphs.Add(trimmed);
                }
            }

            return paragraphs;
        }

        private static string FindDocument(string baseName)
        {
            // 配布フォルダ（VRCast.exe の隣）の .txt、次にエディター用のリポジトリ直下（Unity プロジェクトの 1 つ上）
            string appFolder = Path.GetDirectoryName(Application.dataPath);
            string repository = appFolder != null ? Path.GetDirectoryName(appFolder) : null;
            string[] candidates =
            {
                appFolder != null ? Path.Combine(appFolder, baseName + ".txt") : null,
                repository != null ? Path.Combine(repository, baseName) : null,
            };

            // 最初に見つかったもの
            foreach (string candidate in candidates)
            {
                if (candidate != null && File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
