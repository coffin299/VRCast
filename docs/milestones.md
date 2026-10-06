# Milestones

小さな縦切りで進める: 設計 → 小さな実装 → ビルド → 確認 → 次の実装。

| # | 名称 | 状態 |
| :--- | :--- | :--- |
| 0 | プロジェクト基盤 | 完了 |
| 1 | Basic Avatar Runtime | 完了（既知の課題あり） |
| 2 | Transparent Rendering | 完了 |
| 3 | Expressions | 完了 |
| 4 | Runtime Physics | 完了（既知の課題あり） |
| 5 | Tracking | 完了（MediaPipe: 顔・腕・手・視線・ウインク） |
| 6 | OSC | 見送り（要望があれば実装） |
| 7 | Avatar Conversion Pipeline | 完了（Constraint） |
| 8 | Advanced Output | 仮想カメラ実装済み（Spout / NDI / OBS WebSocket は要望次第） |

## Milestone 0 — プロジェクト基盤

- Unity 2022.3.22f1 プロジェクト（`VRCast/`）
- Assembly 分離: `VRCast.Runtime` / `VRCast.Editor` / `VRCast.Tests.EditMode`
- `VRCastLog`、`AppSettings` / `SettingsStore`、`AppBootstrap`
- `VRCastBuild`（Windows x64）
- `AssemblyIsolationTests`、`SettingsStoreTests`

成功条件: プロジェクトが開く / EditMode テストが通る / `VRCast.exe` が起動し設定読込ログが出る。

## Milestone 1 — Basic Avatar Runtime

1. `.vrcaster` manifest v0 を確定（[avatar-package.md](avatar-package.md)）
2. `com.vrcast.converter` パッケージ
   - `VRCast.AvatarFormat`: manifest / ファイル配置 / 許可コンポーネント / ハッシュ（Runtime と共有）
   - `VRCast.Converter.Editor`: 複製 → 非標準コンポーネント除去 → AssetBundle → ZIP（VRChat SDK 非依存）
3. Runtime `Avatars/`: `AvatarPackageReader`（ZIP 検証・展開）、`AvatarLoader`（`AssetBundle.LoadFromFileAsync`）、`AvatarSession`
4. Runtime `Cameras/`: `OrbitCameraController`、`UI/`: `MainPanel`（Load / Reload / Unload / Info / FOV / Reset）
5. `App/AppRoot`: 結線、起動引数 `--avatar`、前回アバターの自動読込
6. `AvatarPackageReaderTests`

成功条件: VCC プロジェクトで書き出した `.vrcaster` を `VRCast.exe` で読み込み、Humanoid が正常表示される。

確認項目:

- lilToon 等のシェーダーがマゼンタにならないこと
- Reload / Unload を繰り返してもエラー・メモリリークが無いこと（bundle 二重読込エラーが出ないこと）
- 改ざん・破損パッケージがエラー表示で拒否されること

確認結果: lilToon 系 VRChat アバター（Renderer 34、Humanoid）を VCC プロジェクトから書き出し、`VRCast.exe` で正常表示（マゼンタなし、Sanitize 警告なし）。

既知の課題:

- FX レイヤーのトグルで既定 OFF にしている小物が表示される。
  → 対応済み: `FxDefaultStateBaker` が FX の初期ステートを書き出し時に焼き込む。
  FX 以外（Constraint 制御・ワールド固定ギミック等）で隠している小物は残る。
- 待機アニメーションが無いため T ポーズで表示される。
  → 対応済み（要確認）: Milestone 3 の `PoseController` で腕を下ろした待機ポーズにする。
- カメラのフレーミングが小物を含めた境界で計算される。
  → 対応済み（要確認）: Humanoid は頭・腰・足のボーンから本体の範囲を計算し、縦横とも収まる距離に配置。

### FX 既定状態の焼き込み（近似）

VRChat SDK を参照せず、リフレクションで `VRCAvatarDescriptor` の FX コントローラーと Expression Parameters の既定値を読む。
各レイヤー（重み 0 と Synced を除く）で既定ステートから、既定値で条件が成立する遷移（Any State 優先、最大 16 回）を辿り、
到達ステートのモーションの 0 秒時点の値を複製に適用する。

- 適用するのは GameObject 有効状態（`m_IsActive`）、Renderer 有効状態（`m_Enabled`）、BlendShape、マテリアル差し替えのみ
- BlendShape は書き出し画面の「シーンのブレンドシェイプの値を優先する」（既定 ON、EditorPrefs に保存）が OFF のときだけ適用する
- `SampleAnimation` は使わない（Humanoid のマッスルが既定ポーズに戻る、Constraint 前提の Transform 値で小物がずれるため）
- Transform・マッスル・マテリアルプロパティ（色等）のカーブは対象外

- BlendTree: Direct は重みパラメーターの値、1D は値を挟む 2 つの子を線形補間した重みで、2D は先頭の子を採用。
  レイヤー内で同じプロパティの値を重みで混ぜ、重みの合計が 1 未満なら残りを焼き込み前の値で埋める（マテリアルは最も重い子）
- VRChat 組み込みパラメーター（`IsLocal` 等）は 0 とみなす
- Write Defaults の差や、時間経過で変化するステートは再現しない

## Milestone 2 — Transparent Rendering

- Runtime `Rendering/RenderingController`: 背景（透過 = alpha 0 単色 / 不透明単色）、解像度、ディレクショナルライト
- `UI/RenderingSection`: Transparent 切替、背景色 RGB、解像度プリセット、ライト強度・方位・仰角
- `AppSettings` に背景・ライト設定を追加（後方互換）。終了時にウィンドウサイズも保存
- `VRCastBuild`: ウィンドウモード、リサイズ可、Run In Background / Visible In Background を設定
- OBS: ゲームキャプチャ +「透過を許可」で取り込む（ウィンドウキャプチャは透過非対応）

成功条件: 透過背景のアバターを OBS でキャプチャできる。

確認結果: OBS ゲームキャプチャ（透過を許可）で背景が抜けることを確認。操作パネルは映るため配信時は Tab で隠す。
FX 焼き込み（表示 ON/OFF・BlendShape・マテリアルのみ）適用後も T ポーズ・小物位置が維持されることを確認。

確認項目:

- OBS ゲームキャプチャ（透過を許可）で背景が抜けること
- 半透明マテリアル部分の見え方（alpha ブレンドがフレームバッファ alpha にも書かれるため、縁が薄くなる可能性）
- VRCast が非アクティブでも描画が止まらないこと

デスクトップ上でウィンドウ自体を透過させる（デスクトップマスコット表示）は対象外。必要になれば DWM / Win32 API で別途対応する。

## Milestone 3 — Expressions

BlendShape、表情プリセット、Animator Parameter、基本 Viseme。

### 3a. 待機ポーズ

- Runtime `Animations/PoseController`: `HumanPoseHandler` で読込時の姿勢（T ポーズ）の筋肉値を基準に、
  `Arm Down-Up` と `Forearm Stretch` だけを補間（他の筋肉・体の位置は基準のまま）
- 度合い 0 / 0 では記録しておいたボーンの位置・回転を復元し、リターゲット誤差を出さない
- `AppSettings.poseArmDown` / `poseElbowBend`（0〜1）で永続化、UI に Arms down / Elbow bend / Attention / Relaxed / T-Pose
- 追加: 既定を気を付け（Arms down 1 / Elbow bend 0）に変更。筋肉値はアバターの可動域（通常 -60°）までしか下がらないため、
  腕は `Arm Down-Up` ではなく上腕ボーンを直接回し、Arms down 1 で真下から外側へ 12°（`ArmSideAngle`）の向きにする。
  手のひらは体側を向く。Relaxed は Arms down 0.85 / Elbow bend 0.3
- 修正: `SetHumanPose` の体の向きが Body yaw とずれ、Body yaw を回していると Attention / Relaxed で体が反転していたため、
  適用後に腰（Hips）の位置・回転を記録値へ戻す
- 非 Humanoid は対象外

### 3b. 表情プリセット

- Converter `ExpressionExtractor`: FX コントローラー内のクリップのうち、**BlendShape カーブのみ**で構成され
  0 秒時点で重み > 0 を含むものを表情とみなし `metadata/expressions.json` に書き出す（名前順、全 0 のリセット用は除外）
- Runtime: `AvatarPackageReader` が上限付きで読み込み検証。不正・読込不能なら警告して空扱い（アバター表示は継続）
- Runtime `Animations/ExpressionController`: パスと BlendShape 名を解決し、切り替え時は触った BlendShape を
  読込時（FX 焼き込み後）の値へ戻してから適用。数字キー 1〜9 / 0 = Neutral（テキスト入力中は無効）
- UI: Expressions 一覧（3 列、スクロール）。全表情に共通する接頭辞（`_` / `-` / 空白区切り）はボタン表示から省く

確認結果: Marycia で Relaxed ポーズ・表情ボタンの切り替えが動作することを確認。

確認項目:

- Relaxed で腕が体に刺さらず自然に下りること（筋肉値 `ArmDownMuscle` / `ElbowBentMuscle` は要調整の可能性）
- 表情ボタン・数字キーで顔が切り替わり、Neutral で元に戻ること
- 表情以外のクリップ（小物トグル等）が一覧に混ざりすぎないこと

### 3c. 自動まばたき・マイクリップシンク

- Converter `VrcDescriptorReader.GetDescriptorData`: Descriptor の Lip Sync（`VisemeBlendShape` / `JawFlapBlendShape`）と
  Eyelids（BlendShape 方式の blink）を `metadata/descriptor.json` に書き出す（ボーン方式は未対応）。
  Eyelids 未設定のアバター（FX でまばたきするもの）は顔メッシュから `まばたき` / `blink` / `eyeBlinkLeft`+`Right` 等を推定
- Runtime `Audio/MicrophoneInput`: 選択デバイス（空 = 既定）のループ録音から直近 1024 サンプルの RMS を音量 0〜1 に変換。
  しきい値（Mic gate）と感度（Mic gain）、開きは速く閉じは遅く平滑化。無効時は録音しない。切断時は 3 秒ごとに再試行
- Runtime `Animations/LipSyncController`: Viseme 方式は `aa`、JawFlap 方式は口開閉 BlendShape に音量を上乗せ
- Runtime `Animations/BlinkController`: 2〜6 秒のランダム間隔で blink を閉じる→保持→開く（計 0.22 秒）
- `BlendShapeOverlay`: 元の値（表情等）と上乗せ値の大きい方を書き込む。表情切替で元の値が変わっても追従するため、
  表情・まばたき・口が互いを壊さない（Milestone 5 でトラッキング値を同じ仕組みで重ねる）
- UI `FaceSection`: Auto blink / Lip sync の ON/OFF、マイク選択、Mic gain / Mic gate、音量メーター（設定は保存）

確認項目:

- まばたきが自然な間隔で起き、OFF で止まること
- 話すと口が動き、無音時に閉じること（環境ノイズで開く場合は Mic gate を上げる）
- 表情切替中もまばたき・口が動き、表情が崩れないこと

確認結果: Marycia（Eyelids 未設定、顔メッシュの `まばたき` を推定）で自動まばたき、Viseme `aa` による口パクを確認。

Animator Parameter は OSC と合わせて Milestone 6 で扱う。

カメラによる表情・まばたき・口の制御（VSeeFace 相当）は Milestone 5 で扱う。

## Milestone 4 — Runtime Physics

PhysBone 相当（Bone Chain, Pull, Spring, Stiffness, Gravity, Radius, Collider）。完全互換は目標にしない。

- Converter `PhysBoneExtractor`: `VRCPhysBone` / `VRCPhysBoneCollider` をリフレクションで読み `metadata/physbones.json` に書き出す
  （root / ignore / endpoint / multiChildType / pull / spring / stiffness / gravity / gravityFalloff / immobile / radius / 角度制限 / コライダー）。
  各パラメーターのカーブ（チェーン沿いの倍率）は 9 点サンプリングして書き出し、Runtime は粒子の段数 / 最大段数の位置で評価する。
  Grab / Pose、Parameter 連動、Stretch / Squish は対象外。Hinge / Polar 制限は maxAngleX の円錐で近似
- Runtime `Dynamics/`（`UnityEngine.Physics` と衝突しないよう `VRCast.Dynamics`）
  - `PhysBoneSimulator`: 60Hz 固定ステップ（1 フレーム最大 3 ステップ）、アバター 1 体 4096 粒子まで
  - `PhysBoneChain`: 毎フレーム静止回転へ戻して静止位置を求め、Verlet（慣性 = spring、引き戻し = pull、形状維持 = stiffness、
    重力 = gravity × falloff）→ コライダー → 角度制限 → 長さ拘束、最後に親から順に子粒子方向へ回転
  - `PhysBoneCollider`: 球・カプセル・平面（insideBounds 対応）。粒子の点ではなく**ボーン線分（半径付き）**と
    コライダー芯の最近接点で判定し、めり込み量をてこ比（1/接触位置）で子粒子へ伝える（ボーン数の少ないスカートのすり抜け対策）。
    親粒子が既にコライダー内にある場合は子の点のみで押し出す。押し出し量は速度に含めない（前回位置も同量ずらす）。
    起動時にワールド寸法をログ出力
  - Humanoid ボーンは揺らさない（待機ポーズ・トラッキングと競合させない）
  - multiChildType = ignore で複数の子を持つ Transform は回転させず、その子を位置固定のチェーン起点とする
    （スカート Root 等。起点の回転は子の方向へ向ける）
- UI: Face / Physics セクションに PhysBone ON/OFF（チェーン数表示、設定は保存）
- 確認用に Pose へ Body yaw（アバタールートの向き、`AppSettings.avatarYaw`）を追加。トラッキング導入前は体を回したときの揺れで確認する

確認項目:

- 髪・スカート等が揺れ、静止時に元の形へ戻ること
- 体・脚へのめり込みがコライダーで抑えられること
- 揺れ方の強さ（係数 `PullStrength` / `StiffnessStrength` / momentum は見た目で要調整）

確認結果: Marycia で髪・尻尾・スカート（コート）の揺れと静止時の復帰、脚コライダーによる押し出しを確認。

既知の課題: 体を大きく動かした直後はスカートが脚へ一時的にめり込むことがある（上半身配信では許容範囲）。

## Milestone 5 — Tracking

Tracking インターフェースを完成させ、Provider を 1 種類だけ実装する。

候補: Web カメラによるフェイストラッキング（VSeeFace 相当）。頭の向き・まばたき・口の開閉・視線を
`BlendShapeOverlay` / Humanoid の首・頭ボーンへ適用し、トラッキング中は自動まばたき・マイク口パクより優先する。
トラッカー本体は Runtime に組み込まず外部プロセス（例: OpenSeeFace の UDP 出力）から受信する方式を第一候補とする。

採用: OpenSeeFace（`facetracker.exe`）の UDP 出力を受信する。

- Runtime `Tracking/`
  - `IFaceTrackingProvider` / `FaceTrackingFrame`: Provider 共通の入力（頭の回転・左右の目の開き・口の開き）
  - `OpenSeeFacePacket`: 1 顔 1785 バイトのパケット解析（四元数は `(-y, -x, z, w)` で Unity 座標系へ。非有限値・長さ不足は破棄）
  - `OpenSeeFaceReceiver`: `127.0.0.1:<port>` のみ bind、スレッド無しで Update ポーリング。0.5 秒途絶で無効、bind 失敗は 3 秒ごと再試行
  - `FaceTrackingDriver`: 首 40% / 頭 60% にアバタールート基準で回転（上限 70°、平滑化）。受信開始時の向きを正面とし（後に、頭が 0.5 秒静止してから取る方式へ変更。
    途絶後の再検出では取り直さず、トラッキング ON・入力元・カメラの変更時のみ。再検出直後 0.3 秒は頭の向きを使わない）、
    Calibrate で取り直し。Mirror で Y・Z 軸まわりを反転。揺れものが回転後の頭を基準にするよう他の LateUpdate より先に実行
  - 頭の位置（パケットの位置を `(-y, x, -z)` で Unity 座標系へ）の正面位置からの差分で上半身を傾ける:
    前後 → 前後の傾き、左右 → 横の傾き（1 単位 10° × Body lean、上限 20°）を Spine / Chest で分担し、
    頭の向きがトラッキング値どおりになるよう首（無ければ頭）で傾きを打ち消す。Mirror は左右を反転
  - 追加: 体の動かし方の切替（`BodyMotion`: lean / move / lean + move）。move は腰（Hips）を
    前後・左右・上下に動かす（1 単位 0.1 m × 強さ、上限 0.3 m、足も一緒に動く）。腰の位置は毎フレーム読込時の位置へ戻してから
    アバタールート基準の移動量をワールドで加える（Armature の拡大率に依存しない）。腰の移動で揺れもの（胸・髪等）も揺れる
  - 視線: パケットの 3D 点（66・67 = 右・左の瞳、68・69 = 眼球中心）から目の向きを求め、左右平均の角度を
    キャリブレーション時からの差分で Humanoid の目ボーンへ（強さ Eye gaze、上限 左右 20° / 上下 15°、頭の向き基準）。
    3D 推定失敗のフレーム・両目を閉じている間は直前の視線を保持
  - ウインク: 左右の閉じ具合の差が 0.3 以上のときだけ左右別に渡す（Mirror では本人の右目 → アバターの左目）。
    `BlinkController` は両目用 BlendShape に左右の小さい方、ウインク用に差分を上乗せ（両目用が無ければ片目用だけで閉じる）。
    Converter は `ウィンク` / `ウィンク右`、`wink_L` / `wink_R`、`eyeBlinkLeft` / `eyeBlinkRight` 等をまばたきメッシュから推定（要再エクスポート）
  - まばたき・口は同じ BlendShape へ二重に上乗せしないよう `BlinkController.SetExternal`（左右別、自動まばたきより優先）/
    `LipSyncController.ExternalLevel`（マイクと大きい方）へ渡す
  - `FaceTrackerProcess`: `StreamingAssets/OpenSeeFace/` 以下（再帰検索、最も浅いもの）に同梱した `facetracker.exe`（パス指定があればそちら）を、
    Face tracking ON の間は自動起動。異常終了・起動失敗は 5 秒間隔で再試行。
    カメラ一覧は `-l 1` の出力をバックグラウンドで解析し、カメラはデバイス名で保存して起動時に番号へ解決（未選択なら先頭）。
    出力は読み捨てて最後の 1 行を失敗時に表示。受信 OFF・カメラ / ポート変更・アプリ終了に追従（停止・再起動）
  - OpenSeeFace バイナリはリポジトリに含めず（`.gitignore`）、ビルド前に配置する。`VRCastBuild` は未配置なら警告
- UI: Tracking セクション（ON/OFF、カメラ選択、Refresh cameras、Restart tracker、UDP port、Mirror、Body lean（後に体の動かし方の切替と Body motion へ改名）、
  Calibrate（後に Reset pose へ改名し、目線だけ取り直す Reset gaze を追加）、
  頭の移動量の表示、状態と fps。
  同梱版が無いときだけ facetracker.exe パス入力）。設定は保存。
  `<` `>` の巡回選択はマイク選択と共通の `GuiControls.Selector`
- 眉・口角（特徴量）は未対応

確認項目:

- 頭の上下・左右・傾きが正しい向きで反映されること（Mirror ON/OFF の両方）
- まばたき・口の開閉の追従としきい値（`EyeClosedValue` / `EyeOpenedValue` / MouthOpen 範囲は要調整）
- 途絶時に自動まばたき・マイク口パク・正面の頭へ戻ること
- 前後・左右に体を動かしたとき上半身が正しい向きに傾くこと（`LeanDegreesPerUnit` は Head offset の値を見て要調整）
- 目を左右・上下に動かしたとき目ボーンが正しい向きに動くこと、片目を閉じたとき正しい側の目が閉じること

確認結果: 同梱 OpenSeeFace（v1.20.5、`StreamingAssets/OpenSeeFace/` へ zip を展開）の自動起動・カメラ選択・受信・アバターへの反映を確認。
頭の位置による上半身の傾き（前後・左右の向き、既定の強さ）も確認。

### 追加: MediaPipe への移行と腕・手のトラッキング

手を動かすため、既定の入力元を MediaPipe に変更（OpenSeeFace は代替として残し、Tracking セクションで切替）。

- トラッカー: `Tools/MediaPipeTracker/vrcast_tracker.py`（Face / Pose（lite）/ Hand Landmarker、動画モード、CPU）。
  引数は facetracker と同じ形（`-l 1` / `-c` / `-i` / `-p`、追加で `--no-hands` / `--parent-pid`、後に軽量モード用の `--max-fps`）にして起動処理を共通化。
  VRCast は自分の PID を渡し、トラッカーは親の終了（異常終了を含む）を検出して自分も終了する。
  キャッシュが溜まらないよう、exe はフォルダ形式（`_MEI` 展開なし）、OpenCV の OpenCL とカーネルキャッシュは無効、
  ビルドの中間ファイル・PyInstaller / pip のキャッシュは残さない。
  カメラ名は DirectShow（pygrabber）で取得し、UTF-8 で出力。`build.ps1`（ダブルクリック用の `build.bat` から呼び出し可）で exe 化し、モデル 3 種と一緒に `StreamingAssets/MediaPipeTracker/` へ配置
  （Python 3.12.x、仮想環境は `Tools/MediaPipeTracker/.venv`、中間ファイルは `%LOCALAPPDATA%\VRCast\tracker-build`、`.pyc` を作らない設定）
- 送信形式: 1 フレーム 1 パケットの UTF-8 JSON（プロトコル番号 `v`、顔の変換行列 4×4、BlendShape 51 種、腕 6 点と可視度、本人の左手・右手 21 点）。
  手の左右は体の手首に近い方で決め、体が映っていなければ手の左右ラベル（体のラベルと同じ鏡像基準）で決める
- Runtime:
  - `MediaPipePacket`: 頭の回転は行列の四元数 `(x, y, z, w)` のまま（腕・手と左右をそろえる）・位置 `(x, y, -z)`（cm → dm）、`eyeBlink*` → 目の開き（左右は映像基準のため入れ替え）、`jawOpen` → 口、
    `eyeLook*` → 視線（1.0 = 30°）、腕・手の点は `(-x, -y, z)`（world 座標の x は映像の左向き）。
    腕・手の左右ラベルも鏡像基準のため入れ替えて本人の左右にする。壊れた部分（顔・腕・手）だけを無効にする
  - `HandTrackingDriver`: 腕の向き・手の点は固定の指数平滑（毎秒 15）で追従。
    One Euro フィルター等を試したが腕の揺れが改善しなかったため、軽量な当初の方式に戻した
  - 修正: 当初は腕・手の左右と x、まばたきの左右を MediaPipe の出力どおり本人基準として扱っており、Mirror ON でも鏡像にならなかった。
    トラッカーは MediaPipe の左右のまま送り（体が映っていないときの手のラベルも同じ基準）、変換は `MediaPipePacket` に集約
  - `TrackingReceiver`（旧 `OpenSeeFaceReceiver`）: 入力元に合わせて解析を切替。顔と腕手の途絶は別々に判定
  - `TrackerProcess`（旧 `FaceTrackerProcess`）: 入力元ごとの同梱版、入力元・手の ON/OFF の変更で一覧取得・再起動
  - `HandTrackingDriver`: 上腕 → 前腕 → 手首（手首 → 中指の付け根と、小指 → 人差し指の付け根で決まる向き）→ 指 15 節の順に、
    子ボーンへの向きをトラッキングの点の向きへ回す。肩・肘・手首の可視度が 0.5 未満の腕、映っていない手は 0.3 秒で待機ポーズへ戻す。
    両腕とも待機ポーズの間はボーンに触れず、操作中に待機ポーズが変更されたら記録し直す。Mirror では本人の右腕 → アバターの左腕
- UI: 入力元の切替、Arms / hands の ON/OFF と状態表示
- 追加: Raw view（`TrackingSkeletonView`）。アバターの代わりに受信値を平滑化せず線で表示し、トラッカーの推定とアバターへの反映を切り分けて確認できる
- 修正: Raw view で受信値自体（MediaPipe の推定）が揺れていたため、トラッカー側で送信前に One Euro フィルターを掛ける
  （頭の行列は回転と平行移動を別設定、腕・手・可視度も別設定。見失ったら初期化。CPU 負荷はほぼ無し）。BlendShape は反応を優先し平滑化しない

確認項目:

- 腕を上げ下げ・前後に動かしたときアバターの腕が同じ向きに動くこと（Mirror ON/OFF の両方）
- 指を曲げ伸ばし・開閉したとき指が追従すること、手首の向き（手のひら / 手の甲）が正しいこと
- 腕を画面外へ下ろすと待機ポーズへ戻ること
- MediaPipe での頭の向き・まばたき（左右）・口・視線の向き（BlendShape の左右が本人基準であること）
- CPU 負荷とフレームレート（腕・手 OFF で手の推定が止まること）

確認結果: 同梱 MediaPipe トラッカー（`build.bat` で exe 化）の自動起動・顔・腕・手の追従を確認（良好）。
視線（左右・上下）・左右別ウインク・両目を閉じたときの視線の保持を、Mirror ON/OFF の両方で確認。

## Milestone 6 — OSC

OSC 受信・送信、Parameter Mapping（Milestone 3 から移した Animator Parameter を含む）。OSC 無効でも基本表示は動作すること。

見送り: 現時点で用途が無いため実装しない。GitHub の Issue 等で要望があれば、用途（外部からの表情切替・小物トグル・
VMC プロトコルの受信 / 送信）を決めて着手する。

## Milestone 7 — Avatar Conversion Pipeline

VRChat SDK コンポーネント（Descriptor, PhysBone, Constraint 等）を `metadata/*.json` へ変換。
Descriptor（Milestone 3）・PhysBone（Milestone 4）は対応済みのため、ここでは Constraint を扱う。

- Converter `ConstraintExtractor`: VRC Constraint（リフレクション、SDK 非依存）と Unity 標準の Position / Rotation / Scale /
  Parent / Aim / LookAt Constraint を `metadata/constraints.json` に書き出す（重み・軸・静止値・オフセット・ソース・Aim の上方向・
  Solve In Local Space）。アバター外を指すソース・範囲外の値を含む Constraint は除外。Freeze To World は対象外。
  リフレクションの共通処理（bool / Vector3 フィールド、相対パス）は `ReflectionUtility` に集約
- Runtime `Dynamics/`
  - `ConstraintEvaluator`: 1 Constraint の評価。ソースの重み付き平均（回転は半球を揃えた四元数平均）→ 親空間へ変換 →
    オフセット → 静止値から weight ぶん寄せる → 影響しない軸は現在値を保つ。Parent はソースのローカル空間のオフセット、
    Aim は「aimAxis → ソース方向、upAxis → 上方向」の回転、LookAt は Z 軸をソースへ向けて roll
  - `ConstraintSolver`: トラッキング適用後・揺れもの計算前（実行順 -50）に毎フレーム評価。他の Constraint のターゲット
    （またはその子孫）を参照するものを後に並べる（循環は打ち切り）。アバタールート自身・Humanoid ボーン・
    腰の親（Armature 等）は動かさない（待機ポーズ・トラッキングと取り合うと腕の待機ポーズの記録し直しが毎フレーム起きて揺れるため）
- 書き出し画面の結果表示に Constraint 数を追加

確認項目:

- Constraint で手・頭に追従する小物が、腕・頭のトラッキングに合わせて動くこと
- 書き出し結果の Constraints 数と Runtime ログ（`Resolved n/m constraints`）が一致すること
- 揺れものの付いた小物が Constraint の移動に合わせて揺れること

既知の制限: Constraint のソースが揺れものボーンの場合は 1 フレーム遅れる（Constraint を揺れものより先に評価するため）。

## Milestone 8 — Advanced Output

必要性を確認したうえで Spout / NDI / Virtual Camera / OBS WebSocket。まず仮想カメラを実装（他は要望があれば）。

### 8a. 仮想カメラ

- 方式: [UnityCapture](https://github.com/schellingb/UnityCapture)（zlib License）の DirectShow フィルターをドライバーとして同梱し、
  `UnityCapturePlugin.dll` で描画結果のテクスチャを GPU 上でコピーして渡す（CPU への読み戻しをしないため軽い）。
  VSeeFace の仮想カメラと同じ方式。MediaFoundation の仮想カメラ（Windows 11 のみ・COM DLL の自作が必要）は見送り
- 取得: `Tools/UnityCapture/fetch.ps1` がコミット固定で DLL を取得して `StreamingAssets/UnityCapture/` と `Plugins/UnityCapture/x86_64/` に置く
  （リポジトリには含めない）。未配置ならビルド時に警告
- Runtime `Output/`
  - `VirtualCameraOutput`: メインカメラの `OnRenderImage` で送信（操作パネルは映らない）。受け取る側の解像度へ拡大縮小。
    無効時はコンポーネントを止める。プラグインが無ければ状態表示のみ
  - `VirtualCameraInstaller`: `regsvr32` を管理者権限で実行して 32 / 64 bit のフィルターを「VRCast Camera」の名前で登録・解除。
    登録状態は 64 bit フィルターの CLSID の `InprocServer32` を読み、同梱 DLL のパスと比べる（移動したら Reinstall を促す）
- UI: Output セクション（Virtual camera の ON/OFF、Install / Reinstall / Uninstall driver、状態）

確認項目:

- Install driver で UAC が出て、登録後に Discord / Zoom / OBS（映像キャプチャデバイス）で「VRCast Camera」を選べること
- アバターが映り、操作パネルが映らないこと。ウィンドウサイズを変えても映ること（拡大縮小）
- OBS で映像フォーマットを ARGB にすると透過背景のまま取り込めること
- OFF・アプリ終了で受け取る側の映像が停止表示になること、Uninstall driver で一覧から消えること

## 操作パネルの刷新

項目が増えて縦に画面外へはみ出し、下部の設定（体の動かし方、Output）が操作できなかったため、配置と見た目を作り直す。

- 配置: 左のタブ（Avatar / Pose / Face / Tracking / Display / Output / Settings、後に Start を追加）で切り替え、選んだタブだけを縦スクロールで表示。
  高さは画面に収まる範囲に制限し、位置も画面内に保つ。見出しでドラッグ移動
- 見た目: `UiTheme` がダークテーマ（角丸のカード・ボタン、スイッチ型トグル、細いスライダー・スクロールバー）を実行時に生成。
  フォントは OS の日本語フォント（Yu Gothic UI / Meiryo UI）。各セクションは見出し付きカード（`GuiControls.BeginCard`）で区切る
- 言語: `Loc.T(英語, 日本語, 韓国語, 簡体字, 繁体字)` で表示時に選ぶ。設定 `uiLanguage`（Auto = OS に合わせる / English / Japanese / Korean / ChineseSimplified / ChineseTraditional。韓国語・中国語は 1.1.0 で追加）。
  ランタイム部品が返す状態文（受信状態・トラッカー出力など）は英語のまま
- UI の大きさ: 設定 `uiScale`（0.75〜2）を `GUI.matrix` で適用。Settings タブのプリセットで選ぶ
- Camera と Rendering は Display タブへ統合（`RenderingSection` は `DisplaySection` に置き換え）
- アバターの読み込み: ウィンドウへの `.vrcaster` のドロップ（`Platform/FileDropReceiver`、スタンドアロン実行時のみ）と
  Browse ボタン（`Platform/FileDialog`）を追加。非対応・存在しないファイルは読み込む前に日英でエラー表示

確認項目:

- エクスプローラーから `.vrcaster` をドロップすると読み込まれ、他の形式やフォルダでは日英のエラーが出ること（パネル非表示中は表示される）
- Browse で `.vrcaster` だけが一覧に出て、選ぶと読み込まれること（キャンセルでは何も起きないこと）

- 720p 程度の小さいウィンドウでもパネルが画面内に収まり、全タブの項目がスクロールで操作できること
- OS が日本語の環境で Auto のとき日本語、English / 日本語 を選ぶと即座に切り替わり、再起動後も保持されること
- UI の大きさを変えてもパネル上のマウス操作でカメラが動かないこと（パネル外では動くこと）

## 母音リップシンク

VRChat と同じく、マイクの声の母音（あいうえお）に合わせて Viseme（aa / ih / ou / E / oh）を切り替える。

- 方式: VRChat の Oculus Lipsync は配布条件のある SDK のため使わず、`Audio/VowelAnalyzer` でフォルマント（F1 / F2）を LPC で推定して
  母音の代表値と比べる軽量な近似（追加パッケージなし、1 フレーム数千回の積和）
- `MicrophoneInput` が声の出ている間だけ推定して重みを平滑化、`LipSyncController` が音量 × 重みを各 Viseme へ上乗せ
- 設定: `lipSyncVowels`（既定 ON）、`lipSyncVoiceScale`（声の高さ補正 0.8〜1.3）
- テスト: パルス列を 3 つの共振器に通した合成母音（声の高さ 100〜230Hz で確認）を正しく判定すること

確認項目:

- 「あいうえお」と発声すると口の形が切り替わり、判定中の母音がパネルに出ること
- ずれる場合に Voice pitch で改善すること、OFF では従来どおり aa だけで開閉すること

既知の制限: 子音（PP / FF / TH 等）の口の形は使わない。雑音・BGM・複数人の声が混ざると判定が不安定になる。

## ライティング・ウィンドウ背景の改善

- 環境光: ライティングデータを焼かないビルドでは環境光がほぼ無くアバターが暗かったため、`RenderSettings` を単色環境光にして
  明るさ（Ambient）を設定可能にした（SH も同じ色で直接設定し、lilToon 等の SH を参照するシェーダーにも効かせる）
- 太陽光: 色温度（2500〜10000K）を追加。プリセット Sunny / Soft / Default
- 透過時のウィンドウ背景: クリア色を「色 + alpha 0」にして、ウィンドウ上では目に優しい色、OBS ゲームキャプチャでは透過にする

確認項目:

- Ambient を上げると影側も含めアバター全体が明るくなること、プリセットで切り替わること
- 透過 ON でウィンドウは背景色、OBS ゲームキャプチャ（透過を許可）では背景が抜けること

既知の制限: 半透明部分（髪の毛先など）の縁は背景色と混ざった色で OBS に映る（従来は黒と混ざっていた）。
OBS 側で縁に色が付いて見える場合は背景色を暗くする。

### 追加調整

- 太陽光の向きがアバターの背面から当たっていた（カメラは +Z 側から見るのに、向き 0 が +Z 方向へ照らしていた）ため、
  向きをカメラ正面基準に変更。既定の強さ・環境光を 1.2 に上げ、プリセットも強めに
- 背景色を 1 つに統合（既定ベージュ）。非透過時はそのまま、透過時はウィンドウ上だけに表示
- Settings に全設定のリセット（赤いボタン → 確認の 2 段階、ウィンドウサイズ・最後のアバターは保持）
- ビルドの色空間が Gamma だったため、ライトを強くしてもアバターが VRChat より暗かった。VRChat と同じ Linear に固定
- lilToon 等はライトの明るさをテクスチャの色までに制限するため、Light に「アバターの明るさ」（マテリアルの色の倍率）を追加。
  読み込み時にシェーダーと lilToon の明るさ関連の値（`_LightMinLimit` / `_LightMaxLimit` 等）を Player.log に出す
- 操作パネルの配色をダークから濃いめのベージュ（焦げ茶の文字、キャラメル色のアクセント）に変更し、背景のベージュと揃えた
- パネル下部にリセットボタン（顔の向き / 視線 / 表情 / カメラ）を常に表示。Tracking タブの Reset pose / Reset gaze はこちらへ移動
- 初心者向けの導線: Start タブ（読み込み → 透過 → OBS → パネルを隠す の手順と完了表示）、
  ヘルプページ（日本語 / 英語）を見出しの「?」等から開く
- ヘルプと概要ページは同梱をやめ、`webpage` ブランチ（GitHub Pages: <https://coffin299.github.io/VRCast/>）へ移動
- 配布用 zip を作るバッチ（`Tools/Package/package.bat`）。ビルド一式に LICENSE / NOTICE / 利用者向け README.txt を加え、
  `dist/VRCast-<バージョン>-win64.zip` に出力。同梱トラッカー・仮想カメラが無いビルドは警告
- アバターの明るさの上限を 2.5 から 10 に引き上げ
- Tab でパネルを隠している間は、透過設定が OFF でも OBS のゲームキャプチャで背景を抜く。見出しの案内を大きく「[Tab] 全部隠して透過」に変更
  （ウィンドウ自体の透過は枠付きウィンドウでは白背景になるだけで、OBS のウィンドウキャプチャも透過に対応しないため見送り）

確認項目:

- 既定の設定でアバターの正面に光が当たること
- リセットの確認でキャンセルすると何も変わらず、リセットすると背景・ライト・ポーズ・言語が初期状態になること
- Start タブの手順だけで OBS に透過で映せること、ヘルプがブラウザで開くこと

## 1.0.0 リリース

最初の製品版。以降の変更は `CHANGELOG.txt`（日本語 / 英語、配布 zip に同梱）に記録する。

- バージョンを 1.0.0 に設定（アプリ: `VRCastBuild.AppVersion` → `bundleVersion`、書き出しツール: `package.json`）
- `CHANGELOG.txt` を追加し、配布 zip に同梱
- Cursor 用ルール `.cursor/rules/changelog.mdc`: 利用者に見える変更をしたら CHANGELOG の「未リリース」へ日英で追記する
- 書き出しツールを unitypackage でも配布（`Tools/Package/unitypackage.bat`、配布 zip に同梱）。
  書き出しツールの `.meta` をリポジトリに追加（Git URL で入れたときに Unity が `.meta` の無いファイルを無視していた問題も解消）

## 技術的リスク

- AssetBundle の Unity バージョン非互換 → manifest に `unityVersion` を記録し照合。
- シェーダーバリアント欠落によるマゼンタ表示 → lilToon アバター 1 体で早期検証。
- 巨大テクスチャ・シェーダーによる DoS → サイズ上限とコンポーネント許可リスト。
- 背景透過 → OBS ゲームキャプチャはバックバッファの alpha を使うため、カメラを alpha 0 でクリアする方式を採用。
  シェーダーが alpha を正しく書かない場合は抜けが崩れる（要検証）。
- PhysBone は非公開仕様 → 近似実装、パラメータは JSON で保持。
- FX Animator は VRChat 固有パラメータ依存 → Milestone 3 で BlendShape のみのクリップを表情データへ変換（ジェスチャー条件は未対応）。
