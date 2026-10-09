# VRCast Website

VRCast の Web サイト（概要ページ・ヘルプ・デモ動画）。GitHub Pages でこのブランチのルートを公開する。

- 公開 URL: <https://coffin299.github.io/VRCast/>
- ヘルプ: <https://coffin299.github.io/VRCast/help/>（アプリの「?」はパネルの表示言語に合わせて `?lang=ja` / `en` / `ko` / `zh-Hans` / `zh-Hant` 付きで開く）
- 言語別ページ: `/ja/` `/ko/` `/zh-hans/` `/zh-hant/`（例: <https://coffin299.github.io/VRCast/ja/help/>）
- デモ動画: <https://coffin299.github.io/VRCast/video/>
- サイトマップ: <https://coffin299.github.io/VRCast/sitemap/>（全ページのフッターからリンク）
- アプリ本体のソースは [main ブランチ](https://github.com/coffin299/VRCast/tree/main)

| パス | 内容 |
| :--- | :--- |
| `index.html` | 概要ページ（共通ページ） |
| `help/index.html` | ヘルプ（共通ページ） |
| `video/index.html` | デモ動画の視聴ページ。Google は動画が主役のページでしか動画をインデックスしないため、動画の JSON-LD（`VideoObject`）はここに置く。言語別ページは作らない |
| `sitemap/index.html` | HTML サイトマップ（全言語の全ページとヘルプの各項目へのリンク）。`<!-- sitemap -->` ～ `<!-- /sitemap -->` の間は `tools/build-langs.mjs` が書く（ヘルプの項目は目次 `<nav class="toc">` から取る）。`sitemap.xml` を GitHub Pages がうまく返さないときでも、クローラがリンクをたどって全ページに届くようにするためのもの |
| `ja/` `ko/` `zh-hans/` `zh-hant/` | 言語別ページ（`tools/build-langs.mjs` で生成。**直接編集しない**） |
| `tools/build-langs.mjs` | 言語別ページ・hreflang・`sitemap.xml`・HTML サイトマップの一覧を作るスクリプト（Node.js、依存パッケージなし） |
| `assets/style.css` | 共通スタイル |
| `assets/site.js` | 日本語 / 英語 / 韓国語 / 中国語（簡体字・繁体字）の切り替え（ヘッダーの選択欄。共通ページは `?lang=` > 保存した選択 > 自動。自動（既定、選択欄の「自動（ブラウザ）」で戻せる）はブラウザの優先言語の並びから最初に対応しているもの、どれも対応外なら英語。言語別ページは言語が固定で、選ぶとその言語のページ（英語・自動は共通ページ）へ移る）と、ライト（アプリの背景色と同じベージュ）/ ダークの切り替え（保存した選択 > OS の設定）。`<head>` で読み込み描画前に適用 |
| `assets/icon.png` | アイコン原寸（main の `docs/images/vrcast-icon.png`、1254px）。JSON-LD と画像サイトマップ用で、ページ内では読まない |
| `assets/icon-96.png` / `assets/icon-288.png` | `icon.png` の縮小版。96px はファビコン（Google の検索結果用に 48 の倍数）とヘッダー、288px はトップの大きなアイコンと `apple-touch-icon`。`icon.png` を差し替えたら作り直す |
| `assets/preview.webm` / `assets/preview.mp4` | デモ動画（概要ページ上部と動画のページ。自動再生・ループ・音声トラックなし）。WebM（VP9）を先に、H.264 の MP4 を予備に置く（H.264 を再生できない環境向け）。自動再生をブラウザに止められたら `site.js` が操作ボタンを出す |
| `assets/preview-poster.jpg` | デモ動画の最初のフレーム（読み込み中・再生前の表示と JSON-LD・サイトマップのサムネイル） |
| `assets/screenshot.png` | 概要ページの「画面」と OGP / X のカード画像 |
| `sitemap.xml` | 検索エンジン向けのページ一覧（`tools/build-langs.mjs` で生成。`lastmod` は各ページの最終コミット日、未コミットの変更があれば実行日） |
| `llms.txt` | AI クローラ向けの要約とリンク集（機能が変わったら概要ページと合わせて更新） |
| `version.json` | 最新バージョンの情報（`version` = 最新のバージョン番号、`url` = GitHub のダウンロードページ、`boothUrl` = BOOTH のダウンロードページ）。アプリが起動時に読み、新しければ通知する（GitHub / BOOTH を選べる）。`url` は `https://github.com/coffin299/VRCast/` か `https://coffin299.github.io/VRCast/`、`boothUrl` は `https://coffin299.booth.pm/` で始まるものだけ使われ、無い・許可外ならアプリ内の既定のページを開く。**配布 zip を公開してから更新する**（先に更新すると、まだ落とせないバージョンを通知してしまう） |

文言は同じ場所に `<span class="ja">` / `en` / `ko` / `zh-hans` / `zh-hant` の 5 つを並べて書き、表示中の言語以外は CSS で隠す。
言語ごとにフォントの優先順も変える（漢字の字形が言語で違うため）。

## 言語別ページ

共通ページ（`index.html`・`help/index.html`）は英語で表示され（検索エンジン向け）、ブラウザでは自動で言語が切り替わる。
検索結果にその言語のタイトル・説明・本文を出すため、共通ページから他の言語の文言を取り除いた言語別ページを `/ja/` などに置き、hreflang で互いにつなぐ。

**共通ページを編集したら、必ず次を実行してから commit する**（言語別ページ・hreflang・`sitemap.xml`・HTML サイトマップが更新される）。

```powershell
node tools/build-langs.mjs
```

- 言語別ページのタイトル・説明は `tools/build-langs.mjs` の `PAGES`、ページの名前（パンくず・HTML サイトマップ）は `LANGS` の `labels` に書く。ページを増やすときもここに足す（言語別ページを作らないページは `SHARED_PAGES`）。
- 共通ページの `<!-- hreflang -->` ～ `<!-- /hreflang -->` の間はスクリプトが書き換える。
- 言語別ページから `assets/`・`sitemap.xml`・`sitemap/`・`video/` を指す相対リンクは自動で `../` が足される。それ以外の相対リンク（`help/` など）は同じ言語のページを指す。

## 検索エンジン向け

SEO / AI クローラ向けに、各ページの `<head>` に `description`・`canonical`・hreflang・OGP / X カードを書き、概要ページには JSON-LD（`SoftwareApplication`）、ヘルプ・動画・サイトマップのページには `BreadcrumbList`、動画のページには `VideoObject` を置いている。
Google Search Console の所有権確認は概要ページの `google-site-verification` メタタグで行っている（消すと確認が外れる）。
共通ページは文言が 5 言語とも HTML に入っている（CSS で隠すだけ）ので、JavaScript を実行しないクローラにも全言語が読める。
プロジェクトサイト（`/VRCast/` 配下）なので `robots.txt` はドメイン直下に置けず効かない。`sitemap.xml` は Google Search Console などで送信する。

ビルド不要の静的 HTML（言語別ページは生成したものを commit する）。`.nojekyll` で Jekyll 処理を無効にしている。
アプリの機能を追加・変更したら、ヘルプと概要ページも更新し、`node tools/build-langs.mjs` を実行する。
