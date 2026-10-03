# VRCast Website

VRCast の Web サイト（概要ページ・ヘルプ）。GitHub Pages でこのブランチのルートを公開する。

- 公開 URL: <https://coffin299.github.io/VRCast/>
- ヘルプ: <https://coffin299.github.io/VRCast/help/>（アプリの「?」は `?lang=ja` / `?lang=en` 付きで開く）
- アプリ本体のソースは [main ブランチ](https://github.com/coffin299/VRCast/tree/main)

| パス | 内容 |
| :--- | :--- |
| `index.html` | 概要ページ |
| `help/index.html` | ヘルプ |
| `assets/style.css` | 共通スタイル |
| `assets/site.js` | 日本語 / 英語の切り替え（`?lang=` > 保存した選択 > ブラウザの言語、日本語以外は英語）と、ライト（アプリの背景色と同じベージュ）/ ダークの切り替え（保存した選択 > OS の設定）。`<head>` で読み込み描画前に適用 |
| `assets/icon.png` | アイコン（main の `docs/images/vrcast-icon.png`） |

ビルド不要の静的 HTML。`.nojekyll` で Jekyll 処理を無効にしている。
アプリの機能を追加・変更したら、ヘルプと概要ページも更新する。
