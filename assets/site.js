// VRCast サイト共通: 表示言語（日本語 / 英語 / 韓国語 / 中国語 簡体字・繁体字）とライト / ダークの切り替え
// （<head> で読み込み、描画前に適用する）
(function () {
  // 選択を保存するキー
  var langKey = 'vrcast-site-lang';
  var themeKey = 'vrcast-site-theme';
  var root = document.documentElement;

  // 対応言語（URL の ?lang= と保存値に使う値。アプリの「?」もこの値で開く）
  var langs = ['ja', 'en', 'ko', 'zh-Hans', 'zh-Hant'];

  // 保存値の読み書き（保存できない環境は無視する）
  function load(key) {
    try { return localStorage.getItem(key); } catch (e) { return null; }
  }
  function save(key, value) {
    try { localStorage.setItem(key, value); } catch (e) { }
  }
  function remove(key) {
    try { localStorage.removeItem(key); } catch (e) { }
  }

  // 対応言語なら true
  function supported(lang) {
    return langs.indexOf(lang) >= 0;
  }

  // 「自動」の選択肢の表示名（表示中の言語で書く）
  var autoLabels = {
    'ja': '自動（ブラウザ）',
    'en': 'Auto (browser)',
    'ko': '자동 (브라우저)',
    'zh-Hans': '自动（浏览器）',
    'zh-Hant': '自動（瀏覽器）'
  };

  // ブラウザの言語タグ 1 つを対応言語にする（対応外は null）。中国語は地域・文字で簡体字 / 繁体字を分ける
  function matchLang(tag) {
    var lower = (tag || '').toLowerCase();
    if (lower.indexOf('ja') === 0) {
      return 'ja';
    }
    if (lower.indexOf('ko') === 0) {
      return 'ko';
    }
    if (lower.indexOf('en') === 0) {
      return 'en';
    }
    if (lower.indexOf('zh') === 0) {
      return /hant|tw|hk|mo/.test(lower) ? 'zh-Hant' : 'zh-Hans';
    }
    return null;
  }

  // ブラウザ（OS）の優先言語の並びから、最初に対応しているものを選ぶ（どれも対応外なら英語）
  function fromBrowser() {
    var tags = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language];
    for (var i = 0; i < tags.length; i++) {
      var lang = matchLang(tags[i]);
      if (lang) {
        return lang;
      }
    }
    return 'en';
  }

  // 初期の選択: URL の ?lang= > 保存した選択 > 自動
  function initialChoice() {
    var query = new URLSearchParams(location.search).get('lang');
    if (supported(query)) {
      return query;
    }
    var saved = load(langKey);
    if (supported(saved)) {
      return saved;
    }
    return 'auto';
  }

  // 選択から表示言語を決める（自動はブラウザの言語）
  function resolveLang(choice) {
    return choice === 'auto' ? fromBrowser() : choice;
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

  // 言語の選択欄を今の選択に合わせ、「自動」の表示名を表示言語にそろえる
  function markSelects(choice, lang) {
    document.querySelectorAll('select[data-lang-select]').forEach(function (select) {
      var auto = select.querySelector('option[value="auto"]');
      if (auto) {
        auto.textContent = autoLabels[lang];
      }
      select.value = choice;
    });
  }

  // 選択を反映（ページ全体のクラスと lang 属性。クラスは小文字: lang-zh-hans など）
  function applyLang(choice) {
    var lang = resolveLang(choice);
    langs.forEach(function (each) {
      root.classList.remove('lang-' + each.toLowerCase());
    });
    root.classList.add('lang-' + lang.toLowerCase());
    root.lang = lang;
    markSelects(choice, lang);
  }

  // テーマを反映（CSS は data-theme で配色を切り替える）
  function applyTheme(theme) {
    root.setAttribute('data-theme', theme);
    markButtons('data-theme-choice', theme);
  }

  // 描画前に適用してちらつきを防ぐ
  var choice = initialChoice();
  var theme = initialTheme();
  applyLang(choice);
  applyTheme(theme);

  // 選択欄・ボタンは本文の読み込み後に割り当てる
  document.addEventListener('DOMContentLoaded', function () {
    markSelects(choice, resolveLang(choice));
    markButtons('data-theme-choice', theme);
    // 言語の選択欄: 切り替えて保存（自動は保存値を消してブラウザの言語に戻す）
    document.querySelectorAll('select[data-lang-select]').forEach(function (select) {
      select.addEventListener('change', function () {
        choice = select.value;
        applyLang(choice);
        if (choice === 'auto') {
          remove(langKey);
        } else {
          save(langKey, choice);
        }
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
    // 自動再生の動画: ブラウザが自動再生を止めた（省電力モード・設定など）ときは操作ボタンを出して手動で再生できるようにする
    document.querySelectorAll('video[data-autoplay]').forEach(function (video) {
      // muted 属性だけでは無音扱いにならないブラウザがあるため、プロパティでも無音にする
      video.muted = true;
      var played = video.play();
      // 古いブラウザは play() が Promise を返さない
      if (played && played.catch) {
        played.catch(function () {
          video.controls = true;
        });
      }
    });
  });
})();
