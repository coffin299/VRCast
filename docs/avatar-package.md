# Avatar Package (.vrcaster) — v0

> ステータス: **v0（formatVersion 0）**。開発中のため、Milestone 7 までに互換性のない変更が入る可能性がある。
> 変更時は `formatVersion` を上げる。

## 目的

VRChat アバターを、Unity プロジェクトなしで VRCast Runtime が読み込めるデータへ変換したもの。

## コンテナ

ZIP アーカイブ。拡張子 `.vrcaster`。全エントリは **無圧縮（Stored）** で格納する（bundle は LZ4 圧縮済み）。

```text
MyAvatar.vrcaster
├── manifest.json        必須。パッケージ情報
├── avatar.bundle        必須。Unity AssetBundle（StandaloneWindows64, LZ4）
└── metadata/            任意。VRChat 固有設定を変換した JSON
    ├── expressions.json 表情プリセット（FX から抽出、表情が無ければ省略）
    ├── descriptor.json  リップシンク・まぶた設定（どちらも無ければ省略）
    └── physbones.json   揺れもの（PhysBone・コライダー、無ければ省略）
```

上記以外のエントリ（サブディレクトリ、`.json` 以外の metadata、`..` / `\` / `:` を含む名前）を含むパッケージは拒否される。

## manifest.json

```json
{
    "formatVersion": 0,
    "name": "MyAvatar",
    "unityVersion": "2022.3.22f1",
    "bundleSha256": "64 桁の小文字 16 進",
    "bundleSize": 12345678,
    "createdAt": "2026-10-03T05:00:00.0000000Z"
}
```

| フィールド | 必須 | 内容 |
| :--- | :---: | :--- |
| `formatVersion` | ○ | `0` |
| `name` | ○ | 表示名（1〜128 文字） |
| `unityVersion` | ○ | 書き出しに使った Unity バージョン。Runtime と異なる場合は警告 |
| `bundleSha256` | ○ | `avatar.bundle` の SHA-256（小文字 16 進 64 文字）。キャッシュのディレクトリ名にも使う |
| `bundleSize` | ○ | `avatar.bundle` のバイト数 |
| `createdAt` | - | 書き出し日時（UTC, ISO 8601） |

## metadata/expressions.json

FX コントローラー内の「BlendShape カーブのみ」のクリップを、0 秒時点の値で表情プリセットにしたもの。

```json
{
    "presets": [
        {
            "name": "Smile",
            "values": [
                { "path": "Body", "blendShape": "eye_smile", "weight": 100.0 }
            ]
        }
    ]
}
```

| フィールド | 内容 |
| :--- | :--- |
| `presets[].name` | 表示名（クリップ名、1〜512 文字）。最大 256 件 |
| `presets[].values[].path` | アバタールートからの相対パス（空 = ルート）。SkinnedMeshRenderer を持つこと |
| `presets[].values[].blendShape` | BlendShape 名 |
| `presets[].values[].weight` | 重み 0〜100。1 プリセット最大 512 件 |

任意データのため、Runtime は不正・読込不能でも警告のみでアバター表示を続ける（表情は空扱い）。

## metadata/descriptor.json

VRCAvatarDescriptor の Lip Sync と Eyelids（BlendShape 方式）を変換したもの。

```json
{
    "lipSync": {
        "mode": "visemeBlendShape",
        "meshPath": "Body",
        "visemes": ["vrc.v_sil", "vrc.v_pp", "...（15 個）"],
        "mouthOpenBlendShape": ""
    },
    "eyelids": {
        "meshPath": "Body",
        "blinkBlendShapes": ["blink"],
        "winkLeftBlendShape": "wink_L",
        "winkRightBlendShape": "wink_R"
    }
}
```

| フィールド | 内容 |
| :--- | :--- |
| `lipSync.mode` | `none` / `visemeBlendShape` / `jawFlapBlendShape`（ボーン方式は `none`） |
| `lipSync.meshPath` | Viseme 用メッシュのアバタールートからの相対パス |
| `lipSync.visemes` | 0 個または 15 個（sil, PP, FF, TH, DD, kk, CH, SS, nn, RR, aa, E, I, O, U） |
| `lipSync.mouthOpenBlendShape` | JawFlap 方式の口開閉 BlendShape 名 |
| `eyelids.meshPath` / `blinkBlendShapes` | まばたき用メッシュと BlendShape 名（0〜4 個、左右別なら複数を同時に閉じる。空ならまばたき無し） |
| `eyelids.winkLeftBlendShape` / `winkRightBlendShape` | 片目だけ閉じる BlendShape 名（アバターから見た左右、同じメッシュ。両方揃った場合のみ。空ならウインク無し。`blinkBlendShapes` と同名可） |

Eyelids が Descriptor で未設定（FX アニメーションでまばたきするアバター等）の場合、Converter は Viseme 用の顔メッシュから
`まばたき` / `blink` / `eye_blink` / `eyes_close` / `eye_close` / `Fcl_EYE_Close`（大文字小文字無視）、
または左右の組 `eyeBlinkLeft`+`eyeBlinkRight` / `blink_L`+`blink_R` / `Blink_Left`+`Blink_Right` を優先順に探して書き出す。

ウインクはまばたき用メッシュから左右の組 `ウィンク`+`ウィンク右` / `wink_L`+`wink_R` / `Wink_Left`+`Wink_Right` / `winkL`+`winkR` /
`eyeBlinkLeft`+`eyeBlinkRight` / `blink_L`+`blink_R` / `Blink_Left`+`Blink_Right` / `eye_close_L`+`eye_close_R` /
`Fcl_EYE_Close_L`+`Fcl_EYE_Close_R` を優先順に探す（大文字小文字無視）。

不正な場合は expressions.json と同様に警告のみで既定値（none / まばたき無し）として扱う。

## metadata/physbones.json

`VRCPhysBone` / `VRCPhysBoneCollider` を Runtime の近似シミュレーション用に変換したもの。

```json
{
    "bones": [
        {
            "rootPath": "Armature/Hips/Spine/Chest/Neck/Head/Hair_Back",
            "ignorePaths": [],
            "endpointPosition": { "x": 0.0, "y": 0.05, "z": 0.0 },
            "multiChildType": "ignore",
            "pull": 0.2, "spring": 0.2, "stiffness": 0.2,
            "gravity": 0.1, "gravityFalloff": 0.5, "immobile": 0.0,
            "radius": 0.02,
            "colliders": [0],
            "limitType": "angle", "maxAngle": 60.0,
            "radiusCurve": [0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0],
            "pullCurve": [], "springCurve": [], "stiffnessCurve": [],
            "gravityCurve": [], "immobileCurve": [], "maxAngleCurve": []
        }
    ],
    "colliders": [
        {
            "path": "Armature/Hips/Spine/Chest/Neck/Head",
            "shape": "sphere", "radius": 0.08, "height": 0.0,
            "position": { "x": 0.0, "y": 0.1, "z": 0.0 },
            "rotation": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
            "insideBounds": false
        }
    ]
}
```

| フィールド | 内容 |
| :--- | :--- |
| `bones[].rootPath` / `ignorePaths` | チェーンの root と除外 Transform（アバタールートからの相対パス、除外は最大 64） |
| `bones[].endpointPosition` | 末端 Transform のローカル空間に置く仮想末端（0 なら無し） |
| `bones[].multiChildType` | `ignore` / `first` / `average` |
| `bones[].pull` / `spring` / `stiffness` / `gravityFalloff` / `immobile` | 0〜1 |
| `bones[].gravity` | -1〜1 |
| `bones[].radius` | 粒子半径（0〜10、root のスケールで拡縮） |
| `bones[].colliders` | `colliders` 配列のインデックス（最大 64） |
| `bones[].limitType` / `maxAngle` | `none` / `angle`（Hinge / Polar も円錐近似）、0〜180 度 |
| `bones[].*Curve` | チェーン沿い（root = 0 → 最も深い粒子 = 1）の倍率を等間隔にサンプリングした値（0〜16 個、各 ±10）。空なら倍率 1 |
| `colliders[].shape` | `sphere` / `capsule`（高さは両端の半球込み、ローカル Y 軸方向）/ `plane`（法線はローカル Y 軸） |
| `colliders[].position` / `rotation` | コライダー Transform のローカル空間 |

上限: bones 256、colliders 256。不正な場合は警告のみで揺れもの無しとして扱う。

## avatar.bundle

- アバター Prefab を 1 つだけ含む。アセットパスは固定で `Assets/__VRCastExport/avatar.prefab`。
- 含めるのは **Unity 標準コンポーネントのみ**: Transform, Animator, SkinnedMeshRenderer, MeshRenderer, MeshFilter。
  依存として Mesh, Material, Shader, Texture, Avatar (Humanoid) が含まれる。
- Animator Controller は含めない（VRChat 固有の StateMachineBehaviour を含むため）。表情等は Milestone 3 で metadata 化する。
  代わりに FX レイヤーの初期状態（小物トグル・初期表情等）を書き出し時に GameObject / Renderer の有効状態、BlendShape、マテリアル差し替えへ焼き込む（Transform・ポーズは変更しない）。
- VRChat コンポーネント（Avatar Descriptor, PhysBone, Constraint 等）・自作 MonoBehaviour は含めない。
  Runtime で使う設定は Converter が `metadata/*.json` に変換する（Descriptor・PhysBone は対応済み、Constraint 等は Milestone 7）。

## Runtime 側の検証

| 項目 | 内容 |
| :--- | :--- |
| パッケージサイズ | 1 GiB 以下 |
| エントリ数 | 256 以下 |
| エントリ名 | 上記の許可リストのみ |
| manifest.json | 64 KiB 以下、JSON として有効、全必須フィールドが妥当 |
| avatar.bundle | ZIP 上のサイズ = `bundleSize`、展開量を `bundleSize` で打ち切り、SHA-256 = `bundleSha256` |
| metadata/*.json | 1 MiB 以下、展開量を申告サイズで打ち切り。内容は `IMetadata.Validate` で検証（失敗は警告のみ） |
| 生成後 | 許可リスト外のコンポーネントを破棄（Missing Script は件数を警告） |

展開先: `Application.temporaryCachePath/avatars/<bundleSha256>/avatar.bundle`。同じハッシュのファイルがあれば再利用する。

## AssetBundle を使う場合の既知の問題

- Unity バージョン間で互換性がない（書き出し・Runtime とも 2022.3.22f1 に固定）。
- シェーダーバリアントが bundle ビルド時に削られるとマゼンタ表示になる。
- bundle の中身は Unity 依存のため、将来別フォーマット（glTF/VRM 等）へ移行する可能性がある。
