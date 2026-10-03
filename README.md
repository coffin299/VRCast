# VRCast

VRChat 向け 3D アバターを、Unity プロジェクトごとではなく **アバター単体に近い形** で動かす軽量スタンドアロン Runtime。
VSeeFace のように簡単にアバターを表示・トラッキングし、OBS などの配信ソフトへ出力することを目指す。

```text
VRChat アバター (Unity / VCC プロジェクト)
        ↓  com.vrcast.converter (Editor 専用, 予定)
MyAvatar.vavatar
        ↓
VRCast.exe (Runtime)
        ↓
OBS (Window Capture / Game Capture)
```

利用者は Unity Editor・VCC・VRChat 用プロジェクトを常時起動しておく必要がない設計とする。

## 現在の状態

**Milestone 0（プロジェクト基盤）** を実装済み。アバター表示はまだできない。

| 項目 | 状態 |
| :--- | :--- |
| Runtime / Editor の Assembly 分離 | 済 |
| ログ (`VRCastLog`) | 済 |
| 設定の保存・読込 (`SettingsStore`) | 済 |
| Windows ビルドスクリプト (`VRCastBuild`) | 済 |
| アバター読み込み | Milestone 1 で実装予定 |

ロードマップは [docs/milestones.md](docs/milestones.md) を参照。

## 動作環境

- Windows 10 / 11 (x64)
- 開発時: Unity **2022.3.22f1**（VRChat SDK と同一バージョン。AssetBundle 互換性のため固定）
- Render Pipeline: Built-in

## リポジトリ構成

```text
.
├── docs/                 設計ドキュメント
│   ├── architecture.md   Runtime / Editor 分離と依存ルール
│   ├── avatar-package.md .vavatar フォーマット（ドラフト）
│   └── milestones.md     開発マイルストーン
└── VRCast/               Unity Runtime プロジェクト
    └── Assets/VRCast/
        ├── Runtime/      スタンドアロンで動くコード (VRCast.Runtime)
        ├── Editor/       Editor 専用コード (VRCast.Editor)
        └── Tests/        EditMode テスト
```

## ビルド・テスト

Unity Hub で `VRCast/` フォルダを開くか、以下をコマンドラインで実行する。

```powershell
# EditMode テスト
& "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" -batchmode -projectPath .\VRCast -runTests -testPlatform EditMode -testResults .\VRCast\Logs\editmode.xml -logFile .\VRCast\Logs\test.log

# Windows ビルド (出力: VRCast/Builds/Windows/VRCast.exe)
& "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" -batchmode -quit -projectPath .\VRCast -executeMethod VRCast.Editor.Build.VRCastBuild.BuildWindows -logFile .\VRCast\Logs\build.log
```

Editor 上ではメニュー `VRCast > Build > Windows x64` からもビルドできる。

## 設定ファイル

Runtime の設定は `%USERPROFILE%\AppData\LocalLow\VRCast\VRCast\settings.json` に保存される。
ファイルが存在しない・壊れている場合は既定値で起動する。

## アバターの扱いについて

- `.vavatar` は利用者本人がローカルで使うための変換データであり、アバターの再配布を目的としない。
  各アバターの利用規約に従うこと。
- Runtime はアバターを **データとしてのみ** 扱い、アバター内の任意コードは実行しない。

## License

[MIT](LICENSE)
