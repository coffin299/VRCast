# VRCast

<p align="center"><img src="docs/images/vrcast-icon.png" alt="VRCast" width="160"></p>

VRChat 向け 3D アバターを、Unity プロジェクトごとではなく **アバター単体に近い形** で動かす軽量スタンドアロン Runtime。
VSeeFace のように簡単にアバターを表示・トラッキングし、OBS などの配信ソフトへ出力することを目指す。

```text
VRChat アバター (Unity / VCC プロジェクト)
        ↓  com.vrcast.converter (Editor 専用パッケージ)
MyAvatar.vrcaster
        ↓
VRCast.exe (Runtime)
        ↓
OBS (Window Capture / Game Capture) / 仮想カメラ (Discord / Zoom など)
```

利用者は Unity Editor・VCC・VRChat 用プロジェクトを常時起動しておく必要がない設計とする。

- Web サイト（概要・ヘルプ、日本語 / 英語 / 韓国語 / 中国語 簡体字・繁体字。既定はブラウザの言語に合わせる。ライト / ダーク）: <https://coffin299.github.io/VRCast/>
  （ソースは [`webpage` ブランチ](https://github.com/coffin299/VRCast/tree/webpage)。GitHub Pages でブランチのルートを公開）

## 現在の状態

**バージョン 1.1.0（製品版）**。変更点は [CHANGELOG.txt](CHANGELOG.txt)（日本語 / 英語、配布 zip にも同梱）。

**Milestone 1（Basic Avatar Runtime）**・**Milestone 2（Transparent Rendering）**・**Milestone 3（Expressions）**・**Milestone 4（Runtime Physics）**・**Milestone 5（Tracking）** 完了。
VRChat アバターを書き出して `VRCast.exe` で表示し、待機ポーズ・表情切り替えをしつつ背景透過で OBS に取り込める。

| 項目 | 状態 |
| :--- | :--- |
| Runtime / Editor の Assembly 分離 | 済 |
| ログ (`VRCastLog`) / 設定の保存・読込 (`SettingsStore`) | 済 |
| Windows ビルドスクリプト (`VRCastBuild`) | 済 |
| アバター書き出し (`VRCast > Avatar Exporter`) | 済 |
| FX レイヤー既定状態の焼き込み（小物トグルの初期 ON/OFF） | 済 |
| `.vrcaster` 読み込み・表示・オービットカメラ・最小 UI | 済 |
| Humanoid 骨格基準のカメラフレーミング | 済 |
| 背景透過・解像度プリセット・ライト調整 | 済 |
| 待機ポーズ（既定は気を付け。腕を下ろす・肘の曲げ） | 済 |
| 表情プリセット（FX の BlendShape クリップから抽出、数字キー切替） | 済 |
| 自動まばたき（ON/OFF）・マイクリップシンク（母音 あいうえお） | 済 |
| 揺れもの（PhysBone 近似・コライダー） | 済 |
| カメラトラッキング（OpenSeeFace 同梱: 頭の向き・上半身の傾き・まばたき・口） | 済 |
| 視線（目ボーン）・左右別ウインク | 済 |
| MediaPipe トラッカー（顔 + 腕・手・指、既定の入力元。OpenSeeFace と切替可） | 済 |
| 表情反映（MediaPipe のみ。笑顔・驚き・怒り・悲しみ → 表情プリセット） | 済 |
| 表示言語（英語 / 日本語 / 韓国語 / 中国語 簡体字・繁体字）・クレジットタブ | 済 |
| Constraint（VRC / Unity 標準の Position・Rotation・Scale・Parent・Aim・LookAt） | 済 |
| 仮想カメラ出力（VRCast Camera、Discord / Zoom 等） | 済 |

ロードマップは [docs/milestones.md](docs/milestones.md) を参照。

## 動作環境

- Windows 10 / 11 (x64)
- 開発時: Unity **2022.3.22f1**（VRChat SDK と同一バージョン。AssetBundle 互換性のため固定）
- Render Pipeline: Built-in

## 使い方

### 1. アバターを .vrcaster に書き出す

アバターがある Unity プロジェクト（VCC プロジェクト可、Unity 2022.3.22f1）に Converter パッケージを導入する。

- 配布 zip に入っている `VRCast-Converter.unitypackage` をダブルクリック（または Unity にドラッグ＆ドロップ）して **Import**。
  `Assets/VRCast/Converter/` に入る。更新は新しい版を同じ手順で上書きインポート。
- または Package Manager > `+` > **Add package from git URL...**（Git が必要）
  `https://github.com/coffin299/VRCast.git?path=/Packages/com.vrcast.converter`
- またはローカルのクローンから **Add package from disk...** で `Packages/com.vrcast.converter/package.json` を選択
- unitypackage と Package Manager の両方で入れると同じ名前のアセンブリが重複してエラーになるため、どちらか一方にする。

メニュー `VRCast > Avatar Exporter` を開き、シーン上のアバタールート（Animator 付き）を指定して **Export...**。

- 書き出されるのは Unity 標準コンポーネント（Transform / Animator / Renderer / MeshFilter）とそのメッシュ・マテリアル・シェーダー・テクスチャのみ。
- VRChat コンポーネント・スクリプト・Animator Controller は書き出し用の複製から除去される（元のアバターは変更されない）。
- 除去前に、FX レイヤーの初期状態（Expression Parameters の既定値で到達するステート）から、
  小物の表示 ON/OFF・BlendShape・マテリアル差し替えを焼き込む（ポーズと Transform は変更しない。近似処理のため完全一致ではない）。
- FX 内の BlendShape だけを動かすクリップ（表情クリップ）を表情プリセットとして `metadata/expressions.json` に書き出す。
- Avatar Descriptor の Lip Sync（Viseme / JawFlap BlendShape）と Eyelids（BlendShape）設定を `metadata/descriptor.json` に書き出す。
  Eyelids 未設定の場合は顔メッシュの `まばたき` / `blink` / `eyeBlinkLeft`+`eyeBlinkRight` 等をまばたき用として推定する。
  ウインク用 BlendShape（`ウィンク`+`ウィンク右`、`wink_L`+`wink_R` 等）も推定して書き出す。
- PhysBone / PhysBone Collider の主要パラメーターを `metadata/physbones.json` に書き出す（Runtime で近似的に揺らす）。
- VRC Constraint と Unity 標準の Constraint を `metadata/constraints.json` に書き出す（Runtime で毎フレーム評価。手に持たせた小物等が追従する）。
  アバター外を指すソースと Freeze To World は対象外。
- 書き出し先は Windows スタンドアロン用 AssetBundle。Android (Quest) ビルドターゲットのプロジェクトでは切替に時間がかかる。

### 2. VRCast.exe で表示する

- `.vrcaster` ファイルを VRCast のウィンドウへドラッグ＆ドロップすると読み込む（複数ドロップした場合は最初の `.vrcaster`）。
- または Avatar タブの **Browse...** でファイルを選ぶか、入力欄にパスを入力して **Load**（前後の `"` は自動で除去）。
- `.vrcaster` 以外のファイル・存在しないファイルは読み込まず、表示言語でエラーを表示する
  （パネルを隠していてもドロップに失敗したときは表示される）。
- VRCast を管理者として実行している場合、Windows の制限によりエクスプローラーからのドロップは受け付けられない（Browse を使う）。
- 起動引数でも指定可能: `VRCast.exe --avatar "C:\path\MyAvatar.vrcaster"`
- 最後に読み込んだアバターは次回起動時に自動で読み込まれる。

| 操作 | 内容 |
| :--- | :--- |
| 右ドラッグ | カメラ回転 |
| 中ドラッグ | パン |
| ホイール | ズーム |
| Tab | 操作パネルの表示切替（隠している間は OBS で背景も透過） |
| 1〜9 / 0 | 表情プリセット切替 / ニュートラル |

操作パネルは左のタブ（Start / Avatar / Pose / Face / Tracking / Display / Output / Settings / Credits）で項目を切り替え、内容は縦にスクロールする。
配色は背景のベージュに合わせた濃いめのベージュ（焦げ茶の文字、キャラメル色のアクセント）。
パネル下部には、どのタブでも押せるリセットボタン（**Head** 顔の向き / **Gaze** 視線 / **Expression** 表情をニュートラルへ / **Camera** カメラ）を常に表示する。
パネルは見出し部分をドラッグして移動でき、高さは画面に収まるよう自動で調整される。

- **Start**（はじめに、起動時に開く）: アバターの読み込み → 背景の透過 → OBS への取り込み → パネルを隠す、までを手順で案内する。
  読み込み・透過は完了 / 未完了を表示し、その場のボタン（ファイルを選ぶ / 透過にする）で操作できる。仮想カメラ・トラッキング・口パクのタブへも移動できる。
- **ヘルプ**: 見出しの **?**、Start / Settings の **Open help** で Web のヘルプページ（[coffin299.github.io/VRCast/help](https://coffin299.github.io/VRCast/help/)、日本語 / 英語 / 韓国語 / 中国語 簡体字・繁体字）をブラウザで開く（パネルの表示言語で開く）。
- **表示言語**: パネル上部（見出しの下）の言語ボタン、または **Settings** の Display language で「自動（OS に合わせる）」/ English / 日本語 / 한국어 / 简体中文 / 繁體中文 を選べる
  （既定は自動。OS が日本語なら日本語、韓国語なら韓国語、中国語なら簡体字 / 繁体字、それ以外は英語）。Web サイト・ヘルプページも同じ 5 言語で、既定はブラウザの言語に合わせる（配布物の説明 README.txt は日本語 / 英語）。
- **Credits**（クレジット、タブ列の一番下）: 開発者（ごみぃ）・協力者（Arche_039）のリンクと、ライセンス・NOTICE を GitHub で開くボタン（配布物にも `LICENSE.txt` / `NOTICE.txt` を同梱）
  （配布フォルダにファイルが無い場合は GitHub のファイルを開くボタンになる）。
- **UI の大きさ**: **Settings** の UI size で 75% / 100% / 125% / 150% / 200% を選べる（高解像度ディスプレイ向け）。
- **全設定のリセット**: **Settings** の赤いボタン **Reset all settings** → 確認の **Yes, reset** で全ての設定を初期状態に戻す
  （ウィンドウサイズと最後に開いたアバターは保持。元に戻せない）。
- 以下の説明は英語表示の項目名で記載する（日本語表示では対応する日本語名になる）。

**Pose** タブで、アバターの向き（Body yaw）と、T ポーズから腕を下ろす度合い（Arms down）・肘の曲げ（Elbow bend、Humanoid のみ）を調整できる（設定は保存される）。
既定は気を付けの姿勢（Arms down 1 / Elbow bend 0）。ボタンで Attention（気を付け）/ Relaxed（腕を少し開き肘を軽く曲げる）/ T-Pose に切り替えられる。
ポーズは保存され、次回起動時は前回の値で始まる（初回のみ Attention）。
**Expressions** には書き出し時に抽出した表情が並び、クリックまたは数字キーで切り替えられる。
**Face** タブで揺れもの（PhysBone 近似）、自動まばたき、マイクによる口パク（リップシンク）を ON/OFF できる。マイクは `<` `>` で選択し、
Mic gain（感度）と Mic gate（この音量以下は無音扱い）を Level メーターを見ながら調整する。

**Vowel mouth shapes (A I U E O)**（既定 ON）では、VRChat と同じく声の母音に合わせて Viseme の aa / ih / ou / E / oh を切り替える
（あ = aa、い = ih、う = ou、え = E、お = oh）。

- 母音は声の響き（第 1・第 2 フォルマント）から推定する軽量な近似で、VRChat の Oculus Lipsync とは方式が異なる（追加ライブラリなし）。
- 判定中の母音と推定値（F1 / F2）がパネルに出る。ずれる場合は **Voice pitch**（声の高さ補正）を、声が高い人は右・低い人は左へ動かす。
- Viseme を持たないアバター（JawFlap 方式）や、OFF のときは音量で口を開閉するだけ。一部の母音の Viseme が無い場合は aa で代用する。

**Tracking** タブで Web カメラによるトラッキング（頭の向き・まばたき・口の開閉・視線、MediaPipe では腕・手・指も）を ON にできる。
トラッカーは別プロセスとして同梱し、VRCast が裏で起動して UDP で受信する。入力元は `<` `>` で切り替える。

| 入力元 | 内容 | 実行ファイル |
| :--- | :--- | :--- |
| MediaPipe（既定） | 顔 + 腕・手・指（**Arms / hands** で ON/OFF） | `vrcast_tracker.exe`（[MediaPipe](https://ai.google.dev/edge/mediapipe) を使う同梱ツール） |
| OpenSeeFace | 顔のみ | `facetracker.exe`（[OpenSeeFace](https://github.com/emilianavt/OpenSeeFace)） |

1. **Enable tracking** を ON にすると、カメラ一覧を取得して先頭のカメラで自動起動する。
2. `<` `>` でカメラをデバイス名で選ぶと起動し直す（カメラ名は保存され、次回起動時も同じカメラを使う）。
3. 起動に失敗した場合はトラッカーの最後の出力が表示され、5 秒ごとに再試行する（カメラを他のアプリが使用中など）。
   カメラを解放したら **Restart tracker** ですぐ再試行できる。OFF にするか VRCast を終了するとトラッカーも終了する。

同梱版が無いビルドでは、パス入力欄に入力元の実行ファイル（`vrcast_tracker.exe` / `facetracker.exe`）のフルパスを入力する
（OpenSeeFace は VSeeFace に同梱の `VSeeFace_Data\StreamingAssets\Binary\facetracker.exe` も使用可）。手動で起動してもよい（引数はどちらも同じ）:

```powershell
# カメラ番号とデバイス名の確認
.\vrcast_tracker.exe -l 1
# カメラ 0 を 127.0.0.1:11573 へ送信（--no-hands で手の推定を止める、--parent-pid <PID> でそのプロセスの終了時に自動終了）
.\vrcast_tracker.exe -c 0 -i 127.0.0.1 -p 11573
```

- 腕は肩・肘・手首がカメラに映っている間だけ動き、画面外へ下ろすと待機ポーズ（Pose の Arms down / Elbow bend）へ戻る。
- 指は手が映っている間、曲げ伸ばし・開閉と手首の向きを反映する。単眼カメラのため奥行き方向の動きは不正確になりやすい。

- 受信は `127.0.0.1` のみ（外部からの入力は受け付けない）。ポートはパネルで変更可（既定 11573）。
- 受信開始時の顔の向き・位置を正面とする。ずれたらカメラを見て、パネル下部の **Reset** の **Head**（頭・上半身・目線をまとめて正面に）を押す。
  目線だけずれたときはカメラを見て **Gaze** を押す。**Mirror** で左右の反映を切り替える。
- **Raw view** を ON にすると、アバターの代わりに受信値を VRCast 側で平滑化せずそのまま線で表示する（動作確認用、設定は保存しない）。
  MediaPipe トラッカーは頭・腕・手・可視度を One Euro フィルターで平滑化してから送る（静止時の揺れを消し、速い動きでは遅れを抑える）。
  腕（本人の左 = 青、右 = 橙、可視度不足でアバターに使わない腕は灰色）、手の 21 点、頭の向き（箱と鼻の線）、
  視線（目からの線、長さ = 目の開き）、口の開き（縦線）を描き、目・口・視線の数値もパネルに出る。
- 体を前後・左右に動かすとアバターの体も動く。動かし方は **Body** の `<` `>` で選ぶ（強さは **Body strength**、0 で無効。Head offset に正面からの移動量が表示される）。
  - **Lean (feet fixed)**: 足を固定して上半身（背骨・胸）を傾ける（既定）
  - **Move (whole body)**: 腰ごと体全体を前後・左右・上下に動かす（足も一緒に動く）。体の揺れに合わせて胸・髪などの揺れものも揺れる
  - **Lean + move**: 両方
- 目の動きは目ボーン（Humanoid の LeftEye / RightEye）に反映される。強さは **Eye gaze**（0 で無効）。
- 片目を閉じるとウインクする（ウインク用 BlendShape を書き出し時に推定できたアバターのみ。無い場合は両目同時のまばたき）。
- トラッキング中は自動まばたきより優先し、口はマイク口パクと大きい方を使う。途絶すると 0.5 秒で元の動作に戻る。
- **Facial expressions**（表情反映、MediaPipe のみ、既定 ON）: Web カメラで検出した表情（Smile 笑顔 / Surprise 驚き / Angry 怒り / Sad 悲しみ）に合わせて、
  Pose タブの表情プリセットを自動で切り替える。表情はトラッカーが送る顔の BlendShape から合成する（笑顔 = 口角、怒り = 眉を下げる、驚き = 眉全体を上げる、悲しみ = 口角を下げる + 眉の内側だけ上げる）。
  - 表情ごとの割り当ては `<` `>` で選ぶ。既定の **Auto** はプリセット名のキーワード（smile / joy / 笑 / 驚 / angry / 怒 / sad / 泣 など）から推定し、
    **None** でその表情は反映しない。割り当てはプリセット名で保存し、別のアバターで同じ名前が無いときは推定に戻る。
  - **Expression threshold**（表情のしきい値、0.1〜0.8、既定 0.3）: 表情の強さがこの値を超えると切り替える（下げると弱い表情でも反応する）。Raw view 中は各表情の強さが同じ目盛りの数値で出るので、それを見て合わせる。
  - ちらつき防止のため、0.3 秒続いた表情だけに切り替え、抜けるときは入るときより低いしきい値を使う。口を大きく開けている間（発話中）は笑顔を出にくくする。
  - 判定結果が変わったときだけ切り替えるので、数字キーなどで手動で選んだ表情は次の変化まで残る。OFF にする・トラッキングが途絶すると、自動で当てた表情だけニュートラルに戻る。

**Display** タブで、カメラの画角（Field of view）・リセット、背景（透過 / 単色）、ウィンドウ解像度（1280x720 / 1920x1080 / 縦長 720x1280 / 1080x1920）、ライトを変更できる。

- **Light**: **Avatar brightness**（アバターの明るさ。マテリアルの色に倍率を掛ける、0.5〜10、1 = そのまま。
  lilToon 等は明るさがテクスチャの色までに制限されライトを強くしても明るくならないため、暗いときはこれを上げる）、
  **Ambient**（環境光、アバター全体を均一に明るくする）、**Sunlight**（太陽光の強さ）、**Sun color (K)**（色温度。低いほど夕日のような暖色、6500K でほぼ白）、
  **Direction** / **Height**（太陽の向き・高さ。向きはカメラ正面からの角度で 0 = 正面から当たる）。アバターが暗いときは Ambient を上げる。
  プリセット **Sunny**（晴れ）/ **Soft**（やわらか、影が薄い）/ **Default** でまとめて切り替えられる。
- **Background color**: 背景色（既定はベージュ、**Beige (default)** で戻せる）。非透過時はそのまま映り、透過 ON の間はウィンドウ上だけに表示される
  （色だけを塗り透過度は 0 のままなので、OBS のゲームキャプチャ（透過を許可）には映らない）。

設定は終了時に保存され、次回起動時に復元される。ウィンドウは枠をドラッグしてサイズ変更できる。

### 3. OBS に取り込む

1. **Display** タブで **Transparent (OBS Game Capture)** を ON にする（VRCast の画面上では背景色のまま表示されるが、OBS では透過される）。Start タブの **Make transparent** でもよい。
2. OBS で **ゲームキャプチャ** ソースを追加し、モード「特定のウィンドウをキャプチャ」で `[VRCast.exe]: VRCast` を選ぶ。
3. **透過を許可** にチェックを入れる。
4. Tab で操作パネルを隠す。

Tab で隠している間は、透過設定が OFF でもゲームキャプチャ（透過を許可）では背景が抜ける（VRCast の画面上は背景色のまま。もう一度 Tab で設定どおりに戻る）。
OBS のウィンドウキャプチャは透過に対応していないため、背景を抜く場合はゲームキャプチャを使う。
透過不要なら単色背景にしてウィンドウキャプチャ + クロマキーでもよい。

### 4. 仮想カメラで使う（Discord / Zoom など）

1. **Output** タブで **Output (VRCast Camera)** を ON にする。
2. 初回だけ **Install driver** を押す（管理者権限の確認が出る）。ドライバーは VRCast フォルダ内の DLL を登録するため、
   VRCast のフォルダを移動・削除する前に **Uninstall driver** を押す（移動した場合は移動先で **Reinstall driver**）。
3. 受け取る側のアプリのカメラ選択で **VRCast Camera** を選ぶ（一覧に出なければそのアプリを再起動）。

- 映るのはカメラの描画結果のみで、操作パネルは映らない。解像度は受け取る側に合わせて拡大縮小される。
- Discord / Zoom などは透過を扱えないため、背景は背景色（Background color）で映る。
  OBS の映像キャプチャデバイスで受ける場合は、映像フォーマットを ARGB にすると透過のまま取り込める。
- DirectShow 方式の仮想カメラ（[UnityCapture](https://github.com/schellingb/UnityCapture)）のため、DirectShow のカメラを
  一覧に出すアプリで使える。他のアプリが同じ UnityCapture を登録している場合は、後から登録した方の名前・場所になる。

## リポジトリ構成

```text
.
├── .cursor/rules/changelog.mdc   CHANGELOG の追記・バージョン更新の手順 (Cursor 用)
├── CHANGELOG.txt                 更新履歴 (日本語 / 英語、配布 zip に同梱)
├── docs/                         設計ドキュメント
│   ├── architecture.md           Runtime / Editor 分離と依存ルール
│   ├── avatar-package.md         .vrcaster フォーマット (v0)
│   ├── milestones.md             開発マイルストーン
│   └── images/vrcast-icon.png    アイコンの元画像 (1254px)
├── Packages/
│   └── com.vrcast.converter/     アバター変換パッケージ
│       ├── Runtime/              共有フォーマット定義 (VRCast.AvatarFormat)
│       └── Editor/               Exporter (VRCast.Converter.Editor)
├── Tools/
│   ├── MediaPipeTracker/         同梱トラッカー (Python + MediaPipe、build.ps1 / build.bat で exe 化)
│   ├── Package/                  配布用 zip・書き出しツールの unitypackage の作成 (package.bat / unitypackage.bat、同梱 README.txt)
│   └── UnityCapture/             仮想カメラ DLL の取得スクリプト (fetch.ps1)
└── VRCast/                       Unity Runtime プロジェクト
    └── Assets/VRCast/
        ├── Branding/AppIcon.png  アプリアイコン (512px、ビルド時に設定)
        ├── Runtime/              スタンドアロンで動くコード (VRCast.Runtime)
        ├── Editor/               Editor 専用コード (VRCast.Editor)
        └── Tests/                EditMode テスト
```

## ビルド・テスト

Unity Hub で `VRCast/` フォルダを開くか、以下をコマンドラインで実行する（リポジトリ直下で実行し、Editor は閉じておく）。

```powershell
# EditMode テスト
& "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" -batchmode -projectPath .\VRCast -runTests -testPlatform EditMode -testResults .\VRCast\Logs\editmode.xml -logFile .\VRCast\Logs\test.log

# Windows ビルド (出力: VRCast/Builds/Windows/VRCast.exe)
& "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" -batchmode -quit -projectPath .\VRCast -executeMethod VRCast.Editor.Build.VRCastBuild.BuildWindows -logFile .\VRCast\Logs\build.log
```

Editor 上ではメニュー `VRCast > Build > Windows x64` からもビルドできる。
ビルド時に `Assets/VRCast/Branding/AppIcon.png` をアプリアイコン（exe・タスクバー・タイトルバー）に設定する。
色空間は VRChat と同じ Linear に設定する（Gamma のままだとアバターが VRChat より暗く見える）。初回は Editor でテクスチャの再インポートが走る。
差し替える場合は同じパスに透過付きの正方形 PNG（512px 以上推奨）を置く。

### MediaPipe トラッカーの同梱

MediaPipe トラッカー（`Tools/MediaPipeTracker/`）は exe 化したものをリポジトリに含めない（サイズが大きいため `.gitignore` 済み）。
ビルド前に一度、Python 3.12（[python.org](https://www.python.org/) 版、`py` ランチャー付き）を入れた環境でリポジトリ直下から実行する:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\MediaPipeTracker\build.ps1
```

または `Tools\MediaPipeTracker\build.bat` をダブルクリック（Python 3.12.x を指定して上と同じ処理を行う。3.12 が無ければその旨を表示して終了）。

- 仮想環境は `Tools\MediaPipeTracker\.venv`（`.gitignore` 済み）に作られる。別バージョンで作られていた場合は作り直す。
- PyInstaller の中間ファイル・キャッシュは `%LOCALAPPDATA%\VRCast\tracker-build` に置き、完了後に削除する（`.pyc` は作らない設定、pip のキャッシュも残さない）。

キャッシュ・一時ファイルが溜まらないようにしている点:

- exe はフォルダ形式（単一ファイル形式は起動のたびに `%TEMP%\_MEIxxxx` へ展開し、強制終了で残り続けるため使わない）。
- OpenCV の OpenCL を無効化し、カーネルのキャッシュ（`%TEMP%\opencv\...`）を書かせない。
- トラッカーの出力は VRCast が読み捨て（最後の 1 行だけ保持）、ログファイルは作らない。
- VRCast が異常終了してもトラッカーが自分で終了する（`--parent-pid`）。カメラを掴んだまま残らない。
- 出力（`vrcast_tracker.exe` 一式とモデル 3 種）は `VRCast/Trackers/MediaPipeTracker/` に置かれ、Unity でのビルド後にビルドの `StreamingAssets` へコピーされる。
  エディターでの実行時も `Trackers/` から起動する。
- `Assets/` の中（`Assets/StreamingAssets/` を含む）には置かないこと。トラッカーの DLL 群を Unity がプラグインとして登録し、
  エディターのスクリプトコンパイルが `OutOfMemoryException` で失敗する。
- 実行ファイルは `MediaPipeTracker/` 以下を再帰的に探す。見つからない場合もビルドは続行し、警告ログを出す。
- 配布時は MediaPipe（Apache-2.0）と同梱ライブラリのライセンス表記を含めること（一覧は [NOTICE](NOTICE)）。

### 仮想カメラ（UnityCapture）の同梱

仮想カメラのドライバーと送信プラグインはリポジトリに含めない（`.gitignore` 済み）。ビルド前に一度、リポジトリ直下から実行する:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\UnityCapture\fetch.ps1
```

- ドライバー（32 / 64 bit）とライセンス表記は `VRCast/Assets/StreamingAssets/UnityCapture/`、
  送信プラグインは `VRCast/Assets/Plugins/UnityCapture/x86_64/` に置かれる（取得元のコミットは固定）。
- 見つからない場合もビルドは続行し、警告ログを出す（仮想カメラは使えない）。

### OpenSeeFace の同梱（任意）

代替の入力元 OpenSeeFace はリポジトリに含めない（サイズが大きいため `.gitignore` 済み）。使う場合はビルド前に
[OpenSeeFace Releases](https://github.com/emilianavt/OpenSeeFace/releases) の zip を展開し、`Binary/`・`models/`・`Licenses/`・`LICENSE` だけを
`VRCast/Trackers/OpenSeeFace/` に置く（`Assets/` の外）。Unity でのビルド後にビルドの `StreamingAssets` へコピーされる。
（`Unity/`・`escapi/`・`dshowcapture/`・`Source/` などは不要。同名 DLL が重複してサイズも増える）

- 実行ファイルは `OpenSeeFace/` 以下を再帰的に探す（展開時のフォルダ階層は問わない）。
- 見つからない場合もビルドは続行し、警告ログを出す（トラッキングはパス指定が必要になる）。
- 配布時は OpenSeeFace と同梱ライブラリのライセンス表記を含めること（一覧は [NOTICE](NOTICE)）。

### 配布用 zip の作成

Windows ビルドの後、`Tools\Package\package.bat` をダブルクリック（またはリポジトリ直下から実行）:

```powershell
.\Tools\Package\package.bat
# バージョン・ビルドフォルダを指定する場合
powershell -ExecutionPolicy Bypass -File .\Tools\Package\package.ps1 -Version 1.0.1 -BuildPath .\VRCast\Builds\Windows
```

- 出力: `dist\VRCast-<バージョン>-win64.zip`（`.gitignore` 済み）。バージョンの既定は Player Settings の Version（`bundleVersion`。
  ビルド時に `VRCastBuild` の `AppVersion` が設定される）。
- 中身: `VRCast\` フォルダにビルド一式 + `LICENSE.txt` / `NOTICE.txt` / `CHANGELOG.txt` / `README.txt`（利用者向けの日英の説明、`Tools/Package/README.txt`）
  + `VRCast-Converter.unitypackage`（書き出しツール）。
- 書き出しツールの unitypackage だけを作る場合は `Tools\Package\unitypackage.bat`（Unity 不要、出力 `dist\VRCast-Converter-<バージョン>.unitypackage`）。
  `Packages/com.vrcast.converter` を `Assets/VRCast/Converter/` に入るよう詰める。GUID はリポジトリの `.meta` を使うため版をまたいで同じ
  （上書きインポートで更新できる）。ファイルを追加したら Unity で一度開いて `.meta` を作り、一緒にコミットする（無いと中止する）。
### バージョンと更新履歴

- バージョンは `VRCastBuild` の `AppVersion` と `Packages/com.vrcast.converter/package.json` の `version` をそろえる。
- 利用者に見える変更（バグ修正・機能追加など）は `CHANGELOG.txt` の先頭「未リリース / Unreleased」に日本語・英語で追記し、
  リリース時にバージョンと日付へ書き換える（手順は `.cursor/rules/changelog.mdc`）。
  Unity が出力する配布不要のフォルダ（`*_BurstDebugInformation_DoNotShip` 等）は除く。
- `VRCast.exe` が無ければ中止。同梱トラッカーや仮想カメラのドライバーが無い場合は警告を出して続行する
  （仮想カメラ入りで配布するなら `Tools\UnityCapture\fetch.ps1` の後にビルドし直す）。
- 作業フォルダは `%LOCALAPPDATA%\VRCast\package-build` に作り、完了後に削除する。

## 設定・キャッシュ

- 設定: `%USERPROFILE%\AppData\LocalLow\VRCast\VRCast\settings.json`
  初回起動時に既定値で作成され、終了時に現在の設定で上書き保存される。壊れている場合は既定値で起動する。
- 展開済みアバターのキャッシュ: `%USERPROFILE%\AppData\Local\Temp\VRCast\VRCast\avatars\`（削除しても次回読込時に再展開される）
- ログ: `%USERPROFILE%\AppData\LocalLow\VRCast\VRCast\Player.log`（`[VRCast]` で始まる行）

## アバターの扱いについて

- `.vrcaster` は利用者本人がローカルで使うための変換データであり、アバターの再配布を目的としない。
  各アバターの利用規約に従うこと。
- Runtime はアバターを **データとしてのみ** 扱い、アバター内の任意コードは実行しない。
  読込時にパッケージ構造・サイズ・ハッシュを検証し、許可リスト外のコンポーネントを除去する。

## License

[Apache License 2.0](LICENSE)

著作権表示と、配布ビルドに同梱するサードパーティ（MediaPipe・OpenCV・OpenSeeFace 等）の一覧は [NOTICE](NOTICE) を参照。
配布する場合は `LICENSE` と `NOTICE` を同梱すること。
