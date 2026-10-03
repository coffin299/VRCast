// VRCast サイト共通: 日本語 / 英語とライト / ダークの切り替え（<head> で読み込み、描画前に適用する）
(function () {
  // 選択を保存するキー
  var langKey = 'vrcast-site-lang';
  var themeKey = 'vrcast-site-theme';
  var root = document.documentElement;

  // 保存値の読み書き（保存できない環境は無視する）
  function load(key) {
    try { return localStorage.getItem(key); } catch (e) { return null; }
  }
  function save(key, value) {
    try { localStorage.setItem(key, value); } catch (e) { }
  }

  // 初期言語: URL の ?lang= > 保存した選択 > ブラウザ（OS）の言語。日本語以外は英語
  function initialLang() {
    var query = new URLSearchParams(location.search).get('lang');
    if (query === 'ja' || query === 'en') {
      return query;
    }
    var saved = load(langKey);
    if (saved === 'ja' || saved === 'en') {
      return saved;
    }
    return (navigator.language || '').toLowerCase().indexOf('ja') === 0 ? 'ja' : 'en';
  }

  // 初期テーマ: 保存した選択 > OS の設定（ライト / ダーク）
  function initialTheme() {
    var saved = load(themeKey);
    if (saved === 'light' || saved === 'dark') {
      return saved;
    }
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
  }

  // 押されている選択肢のボタンだけ強調する
  function markButtons(attribute, value) {
    document.querySelectorAll('[' + attribute + ']').forEach(function (button) {
      button.classList.toggle('active', button.getAttribute(attribute) === value);
    });
  }

  // 言語を反映（ページ全体のクラスと lang 属性）
  function applyLang(lang) {
    root.classList.remove('lang-ja', 'lang-en');
    root.classList.add('lang-' + lang);
    root.lang = lang;
    markButtons('data-lang', lang);
  }

  // テーマを反映（CSS は data-theme で配色を切り替える）
  function applyTheme(theme) {
    root.setAttribute('data-theme', theme);
    markButtons('data-theme-choice', theme);
  }

  // 描画前に適用してちらつきを防ぐ
  var lang = initialLang();
  var theme = initialTheme();
  applyLang(lang);
  applyTheme(theme);

  // ボタンは本文の読み込み後に割り当てる
  document.addEventListener('DOMContentLoaded', function () {
    markButtons('data-lang', lang);
    markButtons('data-theme-choice', theme);
    // 言語ボタン: 切り替えて保存
    document.querySelectorAll('[data-lang]').forEach(function (button) {
      button.addEventListener('click', function () {
        lang = button.getAttribute('data-lang');
        applyLang(lang);
        save(langKey, lang);
      });
    });
    // テーマボタン: 切り替えて保存
    document.querySelectorAll('[data-theme-choice]').forEach(function (button) {
      button.addEventListener('click', function () {
        theme = button.getAttribute('data-theme-choice');
        applyTheme(theme);
        save(themeKey, theme);
      });
    });
  });
})();
