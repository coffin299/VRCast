# Milestones

小さな縦切りで進める: 設計 → 小さな実装 → ビルド → 確認 → 次の実装。

| # | 名称 | 状態 |
| :--- | :--- | :--- |
| 0 | プロジェクト基盤 | 完了 |
| 1 | Basic Avatar Runtime | 完了（既知の課題あり） |
| 2 | Transparent Rendering | 完了 |
| 3 | Expressions | 完了 |
| 4 | Runtime Physics | 完了（既知の課題あり） |
| 5 | Tracking | 進行中（要確認） |
| 6 | OSC | 未着手 |
| 7 | Avatar Conversion Pipeline | 未着手 |
| 8 | Advanced Output | 未着手 |

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
- `SampleAnimation` は使わない（Humanoid のマッスルが既定ポーズに戻る、Constraint 前提の Transform 値で小物がずれるため）
- Transform・マッスル・マテリアルプロパティ（色等）のカーブは対象外

- BlendTree: Direct は重みパラメーター ≥ 0.5 の子をすべて、1D は最も近い閾値の子、2D は先頭の子を採用
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
- `AppSettings.poseArmDown` / `poseElbowBend`（0〜1）で永続化、UI に Arms down / Elbow bend / T-Pose / Relaxed
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
  - `FaceTrackingDriver`: 首 40% / 頭 60% にアバタールート基準で回転（上限 70°、平滑化）。受信開始時（1 秒以上の途絶後も）の向きを正面とし、
    Calibrate で取り直し。Mirror で Y・Z 軸まわりを反転。揺れものが回転後の頭を基準にするよう他の LateUpdate より先に実行
  - まばたき・口は同じ BlendShape へ二重に上乗せしないよう `BlinkController.ExternalClosed`（自動まばたきより優先）/
    `LipSyncController.ExternalLevel`（マイクと大きい方）へ渡す
- UI: Tracking セクション（ON/OFF、UDP port、Mirror、Calibrate、受信状態と fps）。設定は保存
- 視線・眉・左右別ウインクは未対応（次段階）

確認項目:

- 頭の上下・左右・傾きが正しい向きで反映されること（Mirror ON/OFF の両方）
- まばたき・口の開閉の追従としきい値（`EyeClosedValue` / `EyeOpenedValue` / MouthOpen 範囲は要調整）
- 途絶時に自動まばたき・マイク口パク・正面の頭へ戻ること

## Milestone 6 — OSC

OSC 受信・送信、Parameter Mapping（Milestone 3 から移した Animator Parameter を含む）。OSC 無効でも基本表示は動作すること。

## Milestone 7 — Avatar Conversion Pipeline

VRChat SDK コンポーネント（Descriptor, PhysBone, Constraint 等）を `metadata/*.json` へ変換。

## Milestone 8 — Advanced Output

必要性を確認したうえで Spout / NDI / Virtual Camera / OBS WebSocket。

## 技術的リスク

- AssetBundle の Unity バージョン非互換 → manifest に `unityVersion` を記録し照合。
- シェーダーバリアント欠落によるマゼンタ表示 → lilToon アバター 1 体で早期検証。
- 巨大テクスチャ・シェーダーによる DoS → サイズ上限とコンポーネント許可リスト。
- 背景透過 → OBS ゲームキャプチャはバックバッファの alpha を使うため、カメラを alpha 0 でクリアする方式を採用。
  シェーダーが alpha を正しく書かない場合は抜けが崩れる（要検証）。
- PhysBone は非公開仕様 → 近似実装、パラメータは JSON で保持。
- FX Animator は VRChat 固有パラメータ依存 → Milestone 3 で BlendShape のみのクリップを表情データへ変換（ジェスチャー条件は未対応）。
