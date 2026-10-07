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
| `assets/preview.webm` / `assets/preview.mp4` | 概要ページ上部のデモ動画（自動再生・ループ・音声トラックなし）。WebM（VP9）を先に、H.264 の MP4 を予備に置く（H.264 を再生できない環境向け）。自動再生をブラウザに止められたら `site.js` が操作ボタンを出す |
| `assets/preview-poster.jpg` | デモ動画の最初のフレーム（読み込み中・再生前の表示と JSON-LD のサムネイル） |
| `assets/screenshot.png` | 概要ページの「画面」と OGP / X のカード画像 |
| `sitemap.xml` | 検索エンジン向けのページ一覧（ページを足したら追記し、`lastmod` も更新） |
| `llms.txt` | AI クローラ向けの要約とリンク集（機能が変わったら概要ページと合わせて更新） |
| `version.json` | 最新バージョンの情報（`version` = 最新のバージョン番号、`url` = GitHub のダウンロードページ、`boothUrl` = BOOTH のダウンロードページ）。アプリが起動時に読み、新しければ通知する（GitHub / BOOTH を選べる）。`url` は `https://github.com/coffin299/VRCast/` か `https://coffin299.github.io/VRCast/`、`boothUrl` は `https://coffin299.booth.pm/` で始まるものだけ使われ、無い・許可外ならアプリ内の既定のページを開く。**配布 zip を公開してから更新する**（先に更新すると、まだ落とせないバージョンを通知してしまう） |

文言は同じ場所に `<span class="ja">` / `en` / `ko` / `zh-hans` / `zh-hant` の 5 つを並べて書き、表示中の言語以外は CSS で隠す。
言語ごとにフォントの優先順も変える（漢字の字形が言語で違うため）。

デモ動画を差し替えるときは、元動画から 3 つとも作り直す（例: `ffmpeg -i src.mp4 -an -c:v libvpx-vp9 -b:v 0 -crf 36 -row-mt 1 preview.webm`、`ffmpeg -i src.mp4 -an -c:v libx264 -preset slow -crf 24 -pix_fmt yuv420p -movflags +faststart preview.mp4`、`ffmpeg -i src.mp4 -frames:v 1 -q:v 4 preview-poster.jpg`）。
デモ動画・スクリーンショットのアバターは [Marycia マリシア](https://brn.booth.pm/items/6305948)（BeroarN）。差し替えたら概要ページ・`llms.txt` のクレジットも直す。

SEO / AI クローラ向けに、各ページの `<head>` に `description`・`canonical`・OGP / X カードを書き、概要ページには JSON-LD（`SoftwareApplication`）を置いている。
文言は 5 言語とも HTML に入っている（CSS で隠すだけ）ので、JavaScript を実行しないクローラにも全言語が読める。
プロジェクトサイト（`/VRCast/` 配下）なので `robots.txt` はドメイン直下に置けず効かない。`sitemap.xml` は Google Search Console などで送信する。

ビルド不要の静的 HTML。`.nojekyll` で Jekyll 処理を無効にしている。
アプリの機能を追加・変更したら、ヘルプと概要ページも更新する。
