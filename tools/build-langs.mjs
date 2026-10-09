// 言語別ページ（/ja/ /ko/ /zh-hans/ /zh-hant/）・hreflang・sitemap.xml・HTML サイトマップ（sitemap/）を作る
// 共通ページ（index.html・help/index.html。英語 + 自動切り替え）を編集したら `node tools/build-langs.mjs` を実行する
// 言語別ページは共通ページから表示言語以外の文言を取り除いたもの。直接編集しない
import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// サイトのフォルダー（このスクリプトの 1 つ上）と公開 URL
const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SITE = 'https://coffin299.github.io/VRCast/';

// 文言に付ける言語のクラス（<span class="ja"> など）
const LANG_CLASSES = ['ja', 'en', 'ko', 'zh-hans', 'zh-hant'];

// 共通ページの言語（英語。言語の指定が無い検索の受け皿 = x-default も兼ねる）
// name = その言語での言語名、labels = ページの名前（パンくず・HTML サイトマップ）
const DEFAULT_LANG = {
  code: 'en', folder: '', locale: 'en_US', hreflang: ['x-default', 'en'],
  name: 'English', labels: { overview: 'Overview', help: 'Help', video: 'Demo video' }
};

// 言語別ページを作る言語（folder = URL のフォルダー名、hreflang = この言語のページを示す値。中国語は文字と地域の両方で示す）
const LANGS = [
  {
    code: 'ja', folder: 'ja/', locale: 'ja_JP', hreflang: ['ja'],
    name: '日本語', labels: { overview: '概要', help: 'ヘルプ', video: 'デモ動画' }
  },
  {
    code: 'ko', folder: 'ko/', locale: 'ko_KR', hreflang: ['ko'],
    name: '한국어', labels: { overview: '개요', help: '도움말', video: '데모 영상' }
  },
  {
    code: 'zh-Hans', folder: 'zh-hans/', locale: 'zh_CN', hreflang: ['zh-Hans', 'zh-CN'],
    name: '简体中文', labels: { overview: '概要', help: '帮助', video: '演示视频' }
  },
  {
    code: 'zh-Hant', folder: 'zh-hant/', locale: 'zh_TW', hreflang: ['zh-Hant', 'zh-TW', 'zh-HK'],
    name: '繁體中文', labels: { overview: '概要', help: '說明', video: '示範影片' }
  }
];

// 言語別ページを作るページ（path = サイト内のパス、file = 共通ページのファイル、label = labels のキー、meta = 言語ごとのタイトルと説明）
const PAGES = [
  {
    path: '',
    file: 'index.html',
    label: 'overview',
    meta: {
      'ja': {
        title: 'VRCast - VRChat アバターを Unity・VRChat なしで OBS 配信 | 無料 Windows アプリ',
        description: 'VRCast は、VRChat のアバターを Unity や VRChat を開かずに OBS・Discord・Zoom へ映せる無料の Windows アプリです。Web カメラのトラッキング、あいうえおの口パク、背景透過、仮想カメラに対応。VRM もそのまま読み込めます。'
      },
      'ko': {
        title: 'VRCast - Unity·VRChat 없이 VRChat 아바타로 OBS 방송 | 무료 Windows 앱',
        description: 'VRCast는 Unity나 VRChat을 열지 않고 VRChat 아바타를 OBS·Discord·Zoom에 표시하는 무료 Windows 앱입니다. 웹캠 트래킹, 아이우에오 립싱크, 배경 투명, 가상 카메라를 지원하며 VRM도 그대로 불러올 수 있습니다.'
      },
      'zh-Hans': {
        title: 'VRCast - 无需 Unity 和 VRChat，用 VRChat 虚拟形象在 OBS 直播 | 免费 Windows 应用',
        description: 'VRCast 是一款免费的 Windows 应用，无需打开 Unity 或 VRChat，即可将 VRChat 虚拟形象显示到 OBS、Discord 和 Zoom。支持摄像头追踪、A-I-U-E-O 口型同步、透明背景和虚拟摄像头，也可直接加载 VRM。'
      },
      'zh-Hant': {
        title: 'VRCast - 無需 Unity 和 VRChat，用 VRChat 虛擬形象在 OBS 直播 | 免費 Windows 應用程式',
        description: 'VRCast 是一款免費的 Windows 應用程式，無需開啟 Unity 或 VRChat，即可將 VRChat 虛擬形象顯示到 OBS、Discord 和 Zoom。支援網路攝影機追蹤、A-I-U-E-O 口型同步、透明背景和虛擬攝影機，也可直接載入 VRM。'
      }
    }
  },
  {
    path: 'help/',
    file: 'help/index.html',
    label: 'help',
    meta: {
      'ja': {
        title: 'VRCast ヘルプ - 書き出し・OBS 設定・仮想カメラ・トラッキングの使い方',
        description: 'VRCast の使い方。Unity で VRChat アバターを .vrcaster に書き出す手順（VRM はそのまま）、読み込み、OBS で背景を透過して映す設定、仮想カメラ VRCast Camera、Web カメラのトラッキング、困ったときの対処。'
      },
      'ko': {
        title: 'VRCast 도움말 - 내보내기·OBS 설정·가상 카메라·트래킹 사용법',
        description: 'VRCast 사용법. Unity에서 VRChat 아바타를 .vrcaster로 내보내는 방법(VRM은 그대로), 불러오기, OBS에서 배경을 투명하게 표시하는 설정, 가상 카메라 VRCast Camera, 웹캠 트래킹, 문제 해결.'
      },
      'zh-Hans': {
        title: 'VRCast 帮助 - 导出、OBS 设置、虚拟摄像头与追踪的使用方法',
        description: 'VRCast 使用方法：在 Unity 中将 VRChat 虚拟形象导出为 .vrcaster（VRM 可直接使用）、加载、在 OBS 中以透明背景显示的设置、虚拟摄像头 VRCast Camera、摄像头追踪以及故障排除。'
      },
      'zh-Hant': {
        title: 'VRCast 說明 - 匯出、OBS 設定、虛擬攝影機與追蹤的使用方法',
        description: 'VRCast 使用方法：在 Unity 中將 VRChat 虛擬形象匯出為 .vrcaster（VRM 可直接使用）、載入、在 OBS 中以透明背景顯示的設定、虛擬攝影機 VRCast Camera、網路攝影機追蹤以及疑難排解。'
      }
    }
  }
];

// 言語別ページを作らないページ（label があるものは HTML サイトマップにも ?lang= 付きで載せる）
const SHARED_PAGES = [
  { path: 'video/', file: 'video/index.html', label: 'video' },
  { path: 'sitemap/', file: 'sitemap/index.html' }
];

// HTML サイトマップ（一覧を差し込むページ）
const HTML_SITEMAP = 'sitemap/index.html';

// 言語別ページからも共通の場所を指すパス（これ以外の相対リンクは同じ言語のページを指す）
const SHARED_PREFIXES = ['assets/', 'sitemap.xml', 'sitemap/', 'video/'];

// sitemap に載せる画像（概要ページ）と動画（動画のページ）
const SITEMAP_IMAGES = ['assets/screenshot.png', 'assets/icon.png'];
const SITEMAP_VIDEO = {
  page: 'video/',
  thumbnail: 'assets/preview-poster.jpg',
  content: 'assets/preview.mp4',
  title: 'VRCast demo - VRChat avatar with webcam tracking',
  description: 'A VRChat avatar driven by webcam tracking in VRCast, without Unity or VRChat running.',
  duration: 14
};

// HTML の属性値・XML の文字列として安全にする
function escape(text) {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// 開始タグの直後から、対応する閉じタグの直後の位置を返す（同じ名前の入れ子を数える）
function findClose(html, tag, from) {
  const pattern = new RegExp(`<(/?)${tag}\\b[^>]*>`, 'gi');
  pattern.lastIndex = from;
  let depth = 1;
  let match;
  while ((match = pattern.exec(html))) {
    // 開始タグなら 1 段深く、閉じタグなら 1 段浅く
    depth += match[1] ? -1 : 1;
    if (depth === 0) {
      return pattern.lastIndex;
    }
  }
  throw new Error(`閉じタグが見つからない: <${tag}> (${from})`);
}

// keep 以外の言語のクラスが付いた要素を取り除く（1 行まるごとの要素は行ごと消す）
function stripLangs(html, keep) {
  const drop = LANG_CLASSES.filter((each) => each !== keep);
  const opening = /<([a-zA-Z][\w-]*)\b[^>]*\bclass="([^"]*)"[^>]*>/g;
  let out = '';
  let pos = 0;
  let match;
  while ((match = opening.exec(html))) {
    // 取り除く言語のクラスが無い要素は残す
    if (!match[2].split(/\s+/).some((each) => drop.includes(each))) {
      continue;
    }
    let start = match.index;
    let stop = findClose(html, match[1], opening.lastIndex);
    // 要素の前後が空白だけなら、行ごと消して空行を残さない
    const lineStart = html.lastIndexOf('\n', start - 1) + 1;
    const lineEnd = html.indexOf('\n', stop);
    if (lineStart >= pos && lineEnd >= 0
      && /^[ \t]*$/.test(html.slice(lineStart, start)) && /^[ \t\r]*$/.test(html.slice(stop, lineEnd))) {
      start = lineStart;
      stop = lineEnd + 1;
    }
    out += html.slice(pos, start);
    pos = stop;
    // 取り除いた要素の中は調べない
    opening.lastIndex = stop;
  }
  return out + html.slice(pos);
}

// 1 か所だけのはずの置き換え（見つからなければ元のページが変わったので止める）
function replaceOnce(html, pattern, replacement, name) {
  if (!pattern.test(html)) {
    throw new Error(`${name} が見つからない`);
  }
  return html.replace(pattern, () => replacement);
}

// ある meta タグの content を置き換える（無いページは何もしない）
function setMeta(html, attribute, value) {
  const pattern = new RegExp(`(<meta ${attribute} content=")[^"]*(">)`);
  return html.replace(pattern, (all, before, after) => before + escape(value) + after);
}

// あるページの言語ごとの URL
function pageUrl(page, lang) {
  return SITE + lang.folder + page.path;
}

// hreflang の <link> 一式（共通ページと全言語のページを並べる）
function hreflangLinks(page) {
  return [DEFAULT_LANG, ...LANGS]
    .flatMap((lang) => lang.hreflang.map((code) => `<link rel="alternate" hreflang="${code}" href="${pageUrl(page, lang)}">`))
    .join('\n');
}

// <!-- name --> ～ <!-- /name --> の間を差し替える
function fillBlock(html, name, content) {
  const pattern = new RegExp(`<!-- ${name} -->\\r?\\n[\\s\\S]*?<!-- /${name} -->`);
  // 改行コードは元のファイルに合わせる
  const newline = html.includes('\r\n') ? '\r\n' : '\n';
  const body = content.replace(/\n/g, newline);
  return replaceOnce(html, pattern, `<!-- ${name} -->${newline}${body}${newline}<!-- /${name} -->`, `${name} の差し込み位置`);
}

// 共通ページの目次（<nav class="toc"> のページ内リンク）を [{ hash, html }] で返す（目次が無いページは空）
function tocLinks(source) {
  const nav = source.match(/<nav class="toc">([\s\S]*?)<\/nav>/);
  if (!nav) {
    return [];
  }
  return [...nav[1].matchAll(/<a href="(#[^"]+)">([\s\S]*?)<\/a>/g)].map((match) => ({ hash: match[1], html: match[2] }));
}

// 5 言語分の文言から 1 言語の文字だけを取り出す（タグは外し、&amp; などはそのまま）
function textIn(html, lang) {
  return stripLangs(html, lang.code.toLowerCase()).replace(/<[^>]+>/g, '').trim();
}

// HTML サイトマップの一覧（言語ごとに、全ページとヘルプの各項目へのリンク）
function htmlSitemapSections(sources) {
  return [DEFAULT_LANG, ...LANGS].map((lang) => {
    const items = [];
    for (const page of PAGES) {
      // サイトマップは sitemap/ にあるので 1 段上から辿る
      const href = `../${lang.folder}${page.path}`;
      const toc = tocLinks(sources[page.file])
        .map((link) => `        <li><a href="${href}${link.hash}">${textIn(link.html, lang)}</a></li>`);
      const sub = toc.length ? `\n      <ul>\n${toc.join('\n')}\n      </ul>\n    ` : '';
      items.push(`    <li><a href="${href}">${escape(lang.labels[page.label])}</a>${sub}</li>`);
    }
    for (const page of SHARED_PAGES.filter((each) => each.label)) {
      // 共通ページは ?lang= でその言語の表示にする（英語は指定なし）
      const query = lang === DEFAULT_LANG ? '' : `?lang=${lang.code}`;
      items.push(`    <li><a href="../${page.path}${query}">${escape(lang.labels[page.label])}</a></li>`);
    }
    return `<section lang="${lang.code}">\n  <h2>${escape(lang.name)}</h2>\n  <ul>\n${items.join('\n')}\n  </ul>\n</section>`;
  }).join('\n\n');
}

// 言語別ページは 1 段深いフォルダーに置くので、共通の場所を指す相対パスに ../ を足す
function fixSharedPaths(html, page) {
  const base = SITE + page.path;
  return html.replace(/\b(src|href|poster)="([^"]*)"/g, (all, attribute, value) => {
    // 絶対 URL・ページ内リンク・ルートからのパスはそのまま
    if (/^([a-z][a-z0-9+.-]*:|#|\/)/i.test(value)) {
      return all;
    }
    // 共通ページでの行き先を求め、共通の場所なら 1 段上を指す
    const target = new URL(value, base).href;
    const shared = SHARED_PREFIXES.some((prefix) => target.startsWith(SITE + prefix));
    return shared ? `${attribute}="../${value}"` : all;
  });
}

// 共通ページから 1 言語分のページを作る
function buildLangPage(source, page, lang) {
  const meta = page.meta[lang.code];
  const url = pageUrl(page, lang);
  let html = stripLangs(source, lang.code.toLowerCase());
  // 言語・表示の固定（site.js は data-page-lang があれば切り替えでページを移る）
  html = replaceOnce(html, /<html[^>]*>/,
    `<html lang="${lang.code}" class="lang-${lang.code.toLowerCase()}" data-page-lang="${lang.code}">`, '<html>');
  // 生成物であることを書いておく
  html = html.replace(/<!DOCTYPE html>/i, (doctype) => `${doctype}\n<!-- tools/build-langs.mjs で生成（直接編集しない） -->`);
  // タイトル・説明・共有カード
  html = replaceOnce(html, /<title>[^<]*<\/title>/, `<title>${escape(meta.title)}</title>`, '<title>');
  html = setMeta(html, 'name="description"', meta.description);
  html = setMeta(html, 'property="og:title"', meta.title);
  html = setMeta(html, 'property="og:description"', meta.description);
  html = setMeta(html, 'name="twitter:title"', meta.title);
  html = setMeta(html, 'name="twitter:description"', meta.description);
  // 正規 URL は自分自身（言語別ページどうしは hreflang でつなぐ）
  html = replaceOnce(html, /(<link rel="canonical" href=")[^"]*(">)/, `<link rel="canonical" href="${url}">`, 'canonical');
  html = setMeta(html, 'property="og:url"', url);
  // 共有カードの言語: この言語を主に、他を代替にする
  html = html.replace(/<meta property="og:locale:alternate"[^>]*>\r?\n/g, '');
  const alternates = [DEFAULT_LANG, ...LANGS].filter((each) => each !== lang)
    .map((each) => `\n<meta property="og:locale:alternate" content="${each.locale}">`).join('');
  html = html.replace(/<meta property="og:locale" content="[^"]*">/,
    () => `<meta property="og:locale" content="${lang.locale}">${alternates}`);
  // パンくず: この言語のページを指し、名前も訳す
  html = html.replaceAll(`"item": "${SITE}`, `"item": "${SITE}${lang.folder}`);
  html = html.replace(`"name": "${DEFAULT_LANG.labels[page.label]}"`, () => `"name": "${lang.labels[page.label]}"`);
  return fixSharedPaths(html, page);
}

// ページの最終更新日（未コミットの変更があれば今日、無ければ最後のコミットの日）
function lastModified(file) {
  const run = (args) => execFileSync('git', args, { cwd: ROOT, encoding: 'utf8' }).trim();
  const today = new Date().toLocaleDateString('sv-SE');
  if (run(['status', '--porcelain', '--', file])) {
    return today;
  }
  return run(['log', '-1', '--format=%cs', '--', file]) || today;
}

// sitemap.xml の 1 ページ分
function sitemapEntry(url, lastmod, extra) {
  return `  <url>\n    <loc>${url}</loc>\n    <lastmod>${lastmod}</lastmod>\n${extra}  </url>\n`;
}

// sitemap.xml を作る（全ページ・全言語。概要ページには画像、動画のページには動画の情報を付ける）
function buildSitemap() {
  let entries = '';
  for (const page of PAGES) {
    const lastmod = lastModified(page.file);
    for (const lang of [DEFAULT_LANG, ...LANGS]) {
      // 画像は共通の概要ページにだけ付ける（同じ画像を何度も載せない）
      const images = page.path === '' && lang === DEFAULT_LANG
        ? SITEMAP_IMAGES.map((image) => `    <image:image>\n      <image:loc>${SITE}${image}</image:loc>\n    </image:image>\n`).join('')
        : '';
      entries += sitemapEntry(pageUrl(page, lang), lastmod, images);
    }
  }
  for (const page of SHARED_PAGES) {
    // 動画のページには動画の情報を付ける
    const video = page.path === SITEMAP_VIDEO.page
      ? '    <video:video>\n'
        + `      <video:thumbnail_loc>${SITE}${SITEMAP_VIDEO.thumbnail}</video:thumbnail_loc>\n`
        + `      <video:title>${escape(SITEMAP_VIDEO.title)}</video:title>\n`
        + `      <video:description>${escape(SITEMAP_VIDEO.description)}</video:description>\n`
        + `      <video:content_loc>${SITE}${SITEMAP_VIDEO.content}</video:content_loc>\n`
        + `      <video:duration>${SITEMAP_VIDEO.duration}</video:duration>\n`
        + '    </video:video>\n'
      : '';
    entries += sitemapEntry(SITE + page.path, lastModified(page.file), video);
  }
  return '<?xml version="1.0" encoding="UTF-8"?>\n'
    + '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"\n'
    + '        xmlns:image="http://www.google.com/schemas/sitemap-image/1.1"\n'
    + '        xmlns:video="http://www.google.com/schemas/sitemap-video/1.1">\n'
    + entries
    + '</urlset>\n';
}

// 中身が変わったときだけ書く（変わらないファイルの更新日を動かさない）
function writeIfChanged(file, content) {
  const path = join(ROOT, file);
  let current = null;
  try {
    current = readFileSync(path, 'utf8');
  } catch {
    // まだ無いファイル
  }
  if (current === content) {
    return;
  }
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, content, 'utf8');
  console.log(`更新: ${file}`);
}

// 共通ページの hreflang を更新し、言語別ページを書き出す（HTML サイトマップ用に共通ページの中身も取っておく）
const sources = {};
for (const page of PAGES) {
  const source = fillBlock(readFileSync(join(ROOT, page.file), 'utf8'), 'hreflang', hreflangLinks(page));
  sources[page.file] = source;
  writeIfChanged(page.file, source);
  for (const lang of LANGS) {
    writeIfChanged(lang.folder + page.file, buildLangPage(source, page, lang));
  }
}
// HTML サイトマップの一覧を差し込む
const htmlSitemap = readFileSync(join(ROOT, HTML_SITEMAP), 'utf8');
writeIfChanged(HTML_SITEMAP, fillBlock(htmlSitemap, 'sitemap', htmlSitemapSections(sources)));
// 各ページの更新日を反映するため、sitemap.xml は最後に作る
writeIfChanged('sitemap.xml', buildSitemap());
