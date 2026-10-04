# VRCast Website

VRCast の Web サイト（概要ページ・ヘルプ）。GitHub Pages でこのブランチのルートを公開する。

- 公開 URL: <https://coffin299.github.io/VRCast/>
- ヘルプ: <https://coffin299.github.io/VRCast/help/>（アプリの「?」はパネルの表示言語に合わせて `?lang=ja` / `en` / `ko` / `zh-Hans` / `zh-Hant` 付きで開く）
- アプリ本体のソースは [main ブランチ](https://github.com/coffin299/VRCast/tree/main)

| パス | 内容 |
| :--- | :--- |
| `index.html` | 概要ページ |
| `help/index.html` | ヘルプ |
| `assets/style.css` | 共通スタイル |
| `assets/site.js` | 日本語 / 英語 / 韓国語 / 中国語（簡体字・繁体字）の切り替え（ヘッダーの選択欄。`?lang=` > 保存した選択 > 自動。自動（既定、選択欄の「自動（ブラウザ）」で戻せる）はブラウザの優先言語の並びから最初に対応しているもの、どれも対応外なら英語）と、ライト（アプリの背景色と同じベージュ）/ ダークの切り替え（保存した選択 > OS の設定）。`<head>` で読み込み描画前に適用 |
| `assets/icon.png` | アイコン（main の `docs/images/vrcast-icon.png`） |
| `version.json` | 最新バージョンの情報（`version` = 最新のバージョン番号、`url` = GitHub のダウンロードページ、`boothUrl` = BOOTH のダウンロードページ）。アプリが起動時に読み、新しければ通知する（GitHub / BOOTH を選べる）。`url` は `https://github.com/coffin299/VRCast/` か `https://coffin299.github.io/VRCast/`、`boothUrl` は `https://coffin299.booth.pm/` で始まるものだけ使われ、無い・許可外ならアプリ内の既定のページを開く。**配布 zip を公開してから更新する**（先に更新すると、まだ落とせないバージョンを通知してしまう） |

文言は同じ場所に `<span class="ja">` / `en` / `ko` / `zh-hans` / `zh-hant` の 5 つを並べて書き、表示中の言語以外は CSS で隠す。
言語ごとにフォントの優先順も変える（漢字の字形が言語で違うため）。

ビルド不要の静的 HTML。`.nojekyll` で Jekyll 処理を無効にしている。
アプリの機能を追加・変更したら、ヘルプと概要ページも更新する。
