# Avatar Package (.vavatar) — Draft

> ステータス: **ドラフト**。Milestone 1 で v0 を確定する。それまでは互換性を保証しない。

## 目的

VRChat アバターを、Unity プロジェクトなしで VRCast Runtime が読み込めるデータへ変換したもの。

## コンテナ

ZIP アーカイブ。拡張子 `.vavatar`。

```text
MyAvatar.vavatar
├── manifest.json        必須。パッケージ情報
├── avatar.bundle        必須。Unity AssetBundle（アバター Prefab）
└── metadata/            任意。VRChat 固有設定を変換した JSON
    ├── descriptor.json  (予定) 視点位置・Viseme 設定など
    ├── physbones.json   (予定) PhysBone 相当パラメータ
    └── expressions.json (予定) 表情・パラメータ
```

## manifest.json（案）

```json
{
  "formatVersion": 0,
  "name": "MyAvatar",
  "unityVersion": "2022.3.22f1",
  "bundle": {
    "path": "avatar.bundle",
    "sha256": "..."
  }
}
```

## 設計ルール

- `avatar.bundle` には **Unity 標準コンポーネントのみ** を含める
  （Transform, SkinnedMeshRenderer, MeshRenderer, MeshFilter, Animator, Material, Shader, Texture, Mesh, Avatar）。
- VRChat コンポーネント（Avatar Descriptor, PhysBone, Constraint 等）は bundle に含めず、Converter が `metadata/*.json` に変換する。
- 自作 MonoBehaviour も bundle に含めない（スクリプト参照の破損・コード実行経路を避けるため）。

## Runtime 側の検証（予定）

- ZIP エントリのパストラバーサル（`..`、絶対パス）を拒否する。
- 展開サイズ・エントリ数に上限を設ける。
- `unityVersion` が Runtime と一致しない場合は警告または拒否する。
- `sha256` を照合する。
- インスタンス化後、許可リスト外のコンポーネントを除去する。

## AssetBundle を使う場合の既知の問題

- Unity バージョン間で互換性がない。
- シェーダーバリアントが bundle ビルド時に削られるとマゼンタ表示になる。
- bundle の中身は Unity 依存のため、将来別フォーマット（glTF/VRM 等）へ移行する可能性がある。
