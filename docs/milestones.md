# Milestones

小さな縦切りで進める: 設計 → 小さな実装 → ビルド → 確認 → 次の実装。

| # | 名称 | 状態 |
| :--- | :--- | :--- |
| 0 | プロジェクト基盤 | 完了 |
| 1 | Basic Avatar Runtime | 完了（既知の課題あり） |
| 2 | Transparent Rendering | 完了 |
| 3 | Expressions | 進行中（待機ポーズ・表情プリセット） |
| 4 | Runtime Physics | 未着手 |
| 5 | Tracking | 未着手 |
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
- UI: Expressions 一覧（3 列、スクロール）

確認項目:

- Relaxed で腕が体に刺さらず自然に下りること（筋肉値 `ArmDownMuscle` / `ElbowBentMuscle` は要調整の可能性）
- 表情ボタン・数字キーで顔が切り替わり、Neutral で元に戻ること
- 表情以外のクリップ（小物トグル等）が一覧に混ざりすぎないこと

残り: Animator Parameter、基本 Viseme（マイク音量 → 口形状）、まばたき。

## Milestone 4 — Runtime Physics

PhysBone 相当（Bone Chain, Pull, Spring, Stiffness, Gravity, Radius, Collider）。完全互換は目標にしない。

## Milestone 5 — Tracking

Tracking インターフェースを完成させ、Provider を 1 種類だけ実装する。

## Milestone 6 — OSC

OSC 受信・送信、Parameter Mapping。OSC 無効でも基本表示は動作すること。

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
