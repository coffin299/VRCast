# Milestones

小さな縦切りで進める: 設計 → 小さな実装 → ビルド → 確認 → 次の実装。

| # | 名称 | 状態 |
| :--- | :--- | :--- |
| 0 | プロジェクト基盤 | 完了 |
| 1 | Basic Avatar Runtime | 未着手 |
| 2 | Transparent Rendering | 未着手 |
| 3 | Expressions | 未着手 |
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

## Milestone 1 — Basic Avatar Runtime（予定）

1. `.vavatar` manifest v0 を確定（[avatar-package.md](avatar-package.md)）
2. 最小 Exporter（Editor 専用 UPM パッケージ `com.vrcast.converter`）
   - Humanoid Prefab → 非標準コンポーネント除去 → AssetBundle → ZIP
   - この段階では VRChat SDK に依存しない
3. Runtime `Avatar/`: `AvatarPackageReader`（ZIP 検証）、`AvatarLoader`（`AssetBundle.LoadFromFileAsync`）
4. Runtime `Camera/`: オービットカメラ、最小 UI（Load / Reload / Info）

成功条件: `VRCast.exe` で `.vavatar` を読み込み Humanoid が正常表示される。

## Milestone 2 — Transparent Rendering

背景透過、カメラ設定、解像度、基本ライティング。OBS でキャプチャできること。

## Milestone 3 — Expressions

BlendShape、表情プリセット、Animator Parameter、基本 Viseme。

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
- Built-in RP でのウィンドウ透過 → DWM + `preserveFramebufferAlpha` / Win32 API の検証が必要。
- PhysBone は非公開仕様 → 近似実装、パラメータは JSON で保持。
- FX Animator は VRChat 固有パラメータ依存 → Milestone 3 で Expression データへ変換。
