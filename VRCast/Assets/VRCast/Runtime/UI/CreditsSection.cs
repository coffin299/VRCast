using UnityEngine;

namespace VRCast.UI
{
    /// <summary>
    /// Credits タブ（開発者・協力者のリンクと、GitHub のライセンス・NOTICE を開くボタン）。
    /// </summary>
    public class CreditsSection
    {
        // GitHub 上のライセンス・NOTICE
        private const string RepositoryUrl = "https://github.com/coffin299/VRCast/blob/main/";

        // 開発者・協力者（役割の 5 言語表記、名前、リンク先）
        private static readonly Person[] People =
        {
            new Person(new Role("Developer", "開発者", "개발자", "开发者", "開發者"),
                "ごみぃ", "https://x.com/coffin299"),
            new Person(new Role("Collaborator", "協力者", "협력자", "协力者", "協力者"),
                "Arche_039", "https://x.com/Arche_039"),
        };

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
            DrawLicenses();
        }

        private static void DrawPeople()
        {
            GuiControls.BeginCard(Loc.T("Credits", "クレジット", "크레딧", "致谢", "致謝"));

            // 役割と名前（押すとリンク先を開く）
            foreach (Person person in People)
            {
                if (GuiControls.LabeledButton(person.Role.Text, person.Name))
                {
                    Application.OpenURL(person.Url);
                }
            }

            GuiControls.EndCard();
        }

        private static void DrawLicenses()
        {
            string license = Loc.T("License", "ライセンス", "라이선스", "许可证", "授權條款");
            string open = Loc.T("Open on GitHub", "GitHub で開く", "GitHub에서 열기", "在 GitHub 上打开", "在 GitHub 上開啟");
            GuiControls.BeginCard(license);

            // ライセンスと NOTICE はどちらも GitHub のファイルを開く
            if (GuiControls.LabeledButton(license, open))
            {
                Application.OpenURL(RepositoryUrl + "LICENSE");
            }

            if (GuiControls.LabeledButton("NOTICE", open))
            {
                Application.OpenURL(RepositoryUrl + "NOTICE");
            }

            GuiControls.EndCard();
        }
    }
}
