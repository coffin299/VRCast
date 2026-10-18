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
    ├── physbones.json   揺れもの（PhysBone・コライダー、無ければ省略）
    ├── constraints.json Constraint（VRC / Unity 標準、無ければ省略）
    ├── blendshape_sync.json BlendShape の同期（Modular Avatar の Blendshape Sync、無ければ省略）
    ├── particle_groups.json パーティクルをまとめて切り替える単位（FX の切り替え、無ければ省略）
    ├── fx_toggles.json  FX で表示を ON/OFF するオブジェクト（無ければ省略）
    ├── perfect_sync.json        パーフェクトシンク化の目印と設定（アプリで保存したパーフェクトシンク版のみ）
    └── perfect_sync_shapes.bin  パーフェクトシンク化で作った形状の頂点の移動量（同上）
```

上記以外のエントリ（サブディレクトリ、`.json` 以外の metadata（`perfect_sync_shapes.bin` を除く）、`..` / `\` / `:` を含む名前）を含むパッケージは拒否される。
そのため `perfect_sync_shapes.bin` を含むパーフェクトシンク版は、この形式に対応する前の Runtime（2.0.0 より前）では開けない。

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
| `lipSync.mode` | `none` / `visemeBlendShape` / `jawFlapBlendShape` / `visemeAnimation`（ボーン方式は `none`） |
| `lipSync.meshPath` | Viseme 用メッシュのアバタールートからの相対パス |
| `lipSync.visemes` | 0 個または 15 個（sil, PP, FF, TH, DD, kk, CH, SS, nn, RR, aa, E, I, O, U） |
| `lipSync.mouthOpenBlendShape` | JawFlap 方式の口開閉 BlendShape 名 |
| `lipSync.visemePoses` | 0 個または 15 個（`visemeAnimation` では 15 個必須、並びは `visemes` と同じ）。各要素の `values` は `expressions.json` と同じ形の `{ path, blendShape, weight }`（0〜64 個、重みは 0〜100）で、その Viseme を出し切ったときの BlendShape |

Descriptor の Lip Sync で口を動かせない（`none` になる）アバターは、Converter が改変適用後の FX から
VRChat 組み込みの `Viseme` / `Voice` パラメーターを使うレイヤー（遷移の条件・BlendTree・ステートの速さや Motion Time）を探し、
`Viseme` を 1〜14・`Voice` を 1 にして初期状態を辿ったときの BlendShape のうち、無音（`Viseme` 0・`Voice` 0）より 0.5 以上大きいものを
`visemePoses` に書き出す（`visemeAnimation`）。サブステートマシンの中のステートは辿らない。
この値を知らない旧版のアプリでは descriptor.json 全体が不正扱いになり、まばたきも使えない。
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

## metadata/constraints.json

VRC Constraint（`VRCPositionConstraint` / `VRCRotationConstraint` / `VRCScaleConstraint` / `VRCParentConstraint` /
`VRCAimConstraint` / `VRCLookAtConstraint`）と Unity 標準の同種 Constraint を Runtime 評価用に変換したもの。
回転はすべて四元数（オイラー角は Converter が変換）。

```json
{
    "constraints": [
        {
            "type": "parent",
            "targetPath": "Props/Sword",
            "active": true, "weight": 1.0, "localSpace": false,
            "positionAxes": 7, "rotationAxes": 7, "scaleAxes": 7,
            "positionAtRest": { "x": 0.0, "y": 0.0, "z": 0.0 },
            "positionOffset": { "x": 0.0, "y": 0.0, "z": 0.0 },
            "rotationAtRest": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
            "rotationOffset": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
            "scaleAtRest": { "x": 1.0, "y": 1.0, "z": 1.0 },
            "scaleOffset": { "x": 1.0, "y": 1.0, "z": 1.0 },
            "aimAxis": { "x": 0.0, "y": 0.0, "z": 1.0 },
            "upAxis": { "x": 0.0, "y": 1.0, "z": 0.0 },
            "worldUpType": "sceneUp",
            "worldUpVector": { "x": 0.0, "y": 1.0, "z": 0.0 },
            "worldUpPath": "",
            "roll": 0.0, "useUpObject": false,
            "sources": [
                {
                    "path": "Armature/Hips/Spine/Chest/Shoulder_R/UpperArm_R/LowerArm_R/Hand_R",
                    "weight": 1.0,
                    "positionOffset": { "x": 0.0, "y": 0.05, "z": 0.0 },
                    "rotationOffset": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 }
                }
            ]
        }
    ]
}
```

| フィールド | 内容 |
| :--- | :--- |
| `type` | `position` / `rotation` / `scale` / `parent` / `aim` / `lookAt` |
| `targetPath` | 動かす Transform（アバタールートからの相対パス。ルート自身は Runtime で無視） |
| `active` / `weight` | 無効なら評価しない。weight 0〜1（0 = 静止値、1 = ソースどおり） |
| `localSpace` | ソースのローカル値をそのままターゲットのローカル値へ使う（VRC の Solve In Local Space。Aim / LookAt は無視） |
| `positionAxes` / `rotationAxes` / `scaleAxes` | 影響する軸のビットマスク（X = 1, Y = 2, Z = 4）。外れた軸は現在値を保つ |
| `*AtRest` | 重み 0・有効なソースが無いときのローカル値 |
| `positionOffset` / `rotationOffset` / `scaleOffset` | 結果に加える（位置）・掛ける（回転・スケール）オフセット（ターゲットの親空間） |
| `aimAxis` / `upAxis` | Aim: ソースへ向けるローカル軸・上へ向けるローカル軸（長さ 0 不可） |
| `worldUpType` / `worldUpVector` / `worldUpPath` | Aim の上方向: `sceneUp` / `objectUp` / `objectRotationUp` / `vector` / `none` |
| `roll` / `useUpObject` | LookAt: 視線軸まわりの回転（度）、上方向に `worldUpPath` の上を使うか |
| `sources[].path` / `weight` | ソース Transform と重み 0〜1（重み付き平均） |
| `sources[].positionOffset` / `rotationOffset` | Parent 用のソースのローカル空間でのオフセット |

上限: constraints 512、1 つあたり sources 32、位置・スケール ±1000。アバター外を指すソース・範囲外の値を含む Constraint は
Converter が除外する。Freeze To World は対象外（通常の Constraint として評価）。不正な場合は警告のみで Constraint 無しとして扱う。

## metadata/blendshape_sync.json

Modular Avatar の Blendshape Sync（同期先のメッシュの BlendShape を同期元に合わせる設定）。MA はビルド時にアニメーションへ
同期先のカーブを書き足すだけなので、まばたき・口パク・トラッキングのように Runtime が直接書き込む値は同期されない。
Runtime はこの一覧で毎フレーム同期元の値を同期先へ写す（同期先の上限は効く）。

```json
{
    "bindings": [
        { "sourcePath": "Body", "sourceBlendShape": "vrc.v_aa", "targetPath": "Accessory/Chain", "targetBlendShape": "vrc.v_aa" }
    ]
}
```

| フィールド | 内容 |
| :--- | :--- |
| `sourcePath` / `sourceBlendShape` | 同期元の SkinnedMeshRenderer（アバタールートからの相対パス）と BlendShape 名 |
| `targetPath` / `targetBlendShape` | 同期先（MA のコンポーネントを付けたメッシュ）と BlendShape 名（MA で未指定なら同期元と同じ名前） |

上限: bindings 4096。Converter は改変の適用後に無くなったメッシュ・BlendShape、同じ同期先の重複を除く。
Remap カーブは対象外（値をそのまま写す）。不正な場合は警告のみで同期無しとして扱う。

## metadata/particle_groups.json

VRChat の FX のいずれかのクリップで表示 ON/OFF（GameObject の `m_IsActive`）を動かすオブジェクトのうち、
書き出し後も残っていてパーティクル（ParticleSystem）を子孫に含むもの（1.18.7 以降の Converter）。
Runtime は各パーティクルについて、この一覧のうち最も近い祖先（自身を含む）を単位としてまとめて ON/OFF する。
一覧に無い（または旧版で書き出した）パーティクルは、ボーン・メッシュ等を含まない親（エフェクトのフォルダー）までをまとめる。

```json
{
    "paths": [ "Effects/Aura", "Effects/Wings" ]
}
```

| フィールド | 内容 |
| :--- | :--- |
| `paths` | アバタールートからの相対パス（ルート自身 = 空文字は不可、512 文字まで） |

上限: paths 1024。Converter はパスの順に並べる。不正な場合は警告のみで空として扱う（Runtime の推定だけでまとめる）。

## metadata/fx_toggles.json

VRChat の FX のいずれかのクリップで表示 ON/OFF（GameObject の `m_IsActive`）を動かすオブジェクトのうち、
書き出し後も残っているもの（衣装・小物の切り替え等。パーティクルの有無は問わない）。
Runtime はオブジェクトの一覧（Objects タブ）でこれらに印を付け、これらだけの一覧に絞り込めるようにする。
形式・上限・検証は `particle_groups.json` と同じ（`paths` のみ、1024 件まで、パスの順）。不正な場合は警告のみで空として扱う。

```json
{
    "paths": [ "Outfit/Jacket", "Accessories/Hat" ]
}
```

## metadata/perfect_sync.json

Converter は書き出さない。Runtime の **Perfect sync setup**（パーフェクトシンク化）で、元の `.vrcaster` の全エントリを複製し、
このファイルと `perfect_sync_shapes.bin` を足して別名で保存したもの（manifest の `name` は保存したファイル名にする。元のファイルは変更しない）。
目印と設定を持つので、同じファイルから編集を再開できる。

```json
{
    "version": 1,
    "sourceName": "MyAvatar.vrcaster",
    "sourceBundleSha256": "64 桁の小文字 16 進",
    "detail": 1,
    "mirror": true,
    "includeMouthParts": true,
    "absentParts": 0,
    "facePath": "Body",
    "landmarks": [ { "id": 0, "triangle": 1234, "u": 0.25, "v": 0.4, "mirrored": false } ],
    "shapes": [ { "name": "jawOpen", "source": 0, "strength": 1.0 } ],
    "bindings": [ { "name": "eyeBlinkLeft", "path": "Body", "blendShape": "blink_L", "weight": 1.0 } ]
}
```

| フィールド | 内容 |
| :--- | :--- |
| `version` | `1` |
| `sourceName` / `sourceBundleSha256` | 元のファイル名と bundle の SHA-256（表示用。空可） |
| `detail` | 目印の細かさ `0` 最小（約 10 点）/ `1` 標準（約 20 点）/ `2` 詳細（約 40 点） |
| `mirror` / `includeMouthParts` | 左右対称で打つか / 歯・舌のメッシュにも顎の形状を作るか（編集の再開用） |
| `absentParts` | アバターに無い部位のビットの和（`1` 眉 / `2` 鼻 / `4` 口の中、0〜7。省略時 0）。その部位の目印は打たずに推定し、形状は既定で「なし」（編集の再開用） |
| `facePath` | 目印を打った顔メッシュ（アバタールートからの相対パス、空 = ルート） |
| `landmarks[].id` | 目印の種類（0〜255。最小 0〜10、標準 20〜30、詳細 40〜55。未定義の番号は再開時に無視） |
| `landmarks[].triangle` / `u` / `v` | 顔メッシュの三角形の番号と、その中の重心座標（点 = 頂点0 ×(1−u−v) + 頂点1 × u + 頂点2 × v。u, v, u+v は 0〜1） |
| `landmarks[].mirrored` | 左右対称で左側から自動で決めた右側なら `true`（省略時 `false`。再開後も左側に合わせて置き直す。`mirror` が `false` なら無視） |
| `shapes[].name` / `source` / `strength` | ARKit 名ごとの出どころ（`0` 自動生成 / `1` 既存の BlendShape / `2` なし）と強さ 0〜2 |
| `bindings[].name` | 既存の BlendShape を割り当てる ARKit 名（51 種のいずれか。知らない名前は無視） |
| `bindings[].path` / `blendShape` / `weight` | 割り当てる SkinnedMeshRenderer・BlendShape 名・強さ 0〜2（ARKit の値 × 強さで書く） |

上限: landmarks 128、shapes 64、bindings 512。不正な場合は警告のみで空として扱う。
割り当てた ARKit 名は名前の照合（ARKit 名の BlendShape）より優先し、割り当ての無い ARKit 名は従来どおり名前で探す。

## metadata/perfect_sync_shapes.bin

パーフェクトシンク化で作った形状（ARKit 名の BlendShape）の、動く頂点の番号と移動量。
生成の処理を後で改良しても保存済みのアバターの見た目が変わらないよう、目印からの再計算ではなく結果を保存する。
little-endian のバイナリ（.NET の `BinaryWriter`。文字列は 7 ビット可変長の長さ + UTF-8）。

| 順 | 型 | 内容 |
| :--- | :--- | :--- |
| 1 | int32 | 識別子 `0x53504356`（"VCPS"） |
| 2 | int32 | 版 `1` |
| 3 | int32 | 形状の数（0〜1024） |
| 形状ごと | string, string | メッシュのパス（アバタールートからの相対パス）と ARKit 名 |
| | int32 | 動く頂点の数（0〜1048576） |
| 頂点ごと | int32, float32 ×3 | 頂点番号（0 以上）と移動量 x, y, z（メッシュの座標系、±10000 以内） |

上限: 64 MiB。途中で切れている・範囲外の値を含む場合は警告のみで形状無しとして扱う。
Runtime は読み込み時、メッシュを複製して形状を末尾に足し（`AddBlendShapeFrame`、重み 100 で 1 フレーム）、レンダラーの参照を差し替える
（既存の BlendShape の番号は変わらない）。読めないメッシュ・見つからないメッシュ・同じ名前が既にある形状・頂点番号が合わない形状は飛ばす。

## avatar.bundle

- アバター Prefab を 1 つだけ含む。アセットパスは固定で `Assets/__VRCastExport/avatar.prefab`。
- 含めるのは **Unity 標準コンポーネントのみ**: Transform, Animator, SkinnedMeshRenderer, MeshRenderer, MeshFilter,
  ParticleSystem, ParticleSystemRenderer（パーティクルは 1.17.7 以降の Converter から。以前の Runtime は読込時に除去する）。
  シーン上で非アクティブなオブジェクトは、Converter の「非アクティブのオブジェクトも書き出す」（1.18.7 以降、既定 ON）が ON なら非アクティブのまま書き出す
  （FX の初期状態で表示されるものも非アクティブにそろえる。Runtime の Objects タブで ON にできる）。
  OFF（と 1.18.7 より前の Converter）では書き出さず、パーティクルだけを非アクティブのまま残す（その親は Transform のみ。パーティクルは 1.17.7 以降）。
  依存として Mesh, Material, Shader, Texture, Avatar (Humanoid) が含まれる。
  BlendShape のある SkinnedMeshRenderer のメッシュは、書き出し時に頂点を読み取れる（Read/Write 有効の）複製へ差し替える
  （パーフェクトシンク化で顔の形を読むため。1.18.8 より後の Converter）。
- Animator Controller は含めない（VRChat 固有の StateMachineBehaviour を含むため）。表情等は Milestone 3 で metadata 化する。
  代わりに FX レイヤーの初期状態（小物トグル・初期表情等）を書き出し時に GameObject / Renderer の有効状態、BlendShape、マテリアル差し替えへ焼き込む（Transform・ポーズは変更しない。BlendShape は既定ではシーン上の値を優先する）。
- VRChat コンポーネント（Avatar Descriptor, PhysBone, Constraint 等）・自作 MonoBehaviour は含めない。
  Runtime で使う設定は Converter が `metadata/*.json` に変換する（Descriptor・PhysBone・Constraint・MA Blendshape Sync・パーティクルの切り替え単位・FX で切り替えるオブジェクト）。

## Runtime 側の検証

| 項目 | 内容 |
| :--- | :--- |
| パッケージサイズ | 1 GiB 以下 |
| エントリ数 | 256 以下 |
| エントリ名 | 上記の許可リストのみ |
| manifest.json | 64 KiB 以下、JSON として有効、全必須フィールドが妥当 |
| avatar.bundle | ZIP 上のサイズ = `bundleSize`、展開量を `bundleSize` で打ち切り、SHA-256 = `bundleSha256` |
| metadata/*.json | 1 MiB 以下、展開量を申告サイズで打ち切り。内容は `IMetadata.Validate` で検証（失敗は警告のみ） |
| metadata/perfect_sync_shapes.bin | 64 MiB 以下、展開量を申告サイズで打ち切り。形式と値の範囲を検証（失敗は警告のみ） |
| 生成後 | 許可リスト外のコンポーネントを破棄（Missing Script は件数を警告） |

展開先: `Application.temporaryCachePath/avatars/<bundleSha256>/avatar.bundle`。同じハッシュのファイルがあれば再利用する。

## AssetBundle を使う場合の既知の問題

- 別系列の Unity との互換性はない（書き出しは 2022.3.22f1、Runtime は同じ 2022.3 系列の最新 LTS。系列が違えば読込時に警告）。
- シェーダーバリアントが bundle ビルド時に削られるとマゼンタや砂嵐表示になる。書き出し時にアバターのマテリアルから `ShaderVariantCollection` を作り、シェーダー本体と同じ bundle に同梱する（アプリ読込時に WarmUp）。ビルド中は Graphics のライトマップ／フォグ／インスタンス削り込みを一時的に緩め、Optimize Mesh Data を OFF にする（ON のままだと lilToon 等が使う UV1 以降が削られ砂嵐になる Unity 2022 の既知問題）。アクティブなビルドターゲットが Android（Quest）のままだとテクスチャが ASTC 等になり砂嵐になるため、StandaloneWindows64 のときだけ書き出せる。[VRCQuestTools](https://kurotu.github.io/VRCQuestTools/ja/) は Android アクティブ時に `CompressTexture(ASTC)` でテクスチャを焼くことがあり、Standalone 上書きに ASTC / ETC が残る場合もある。書き出し中だけ PC 向け（DXT / Automatic）に直してから bundle 化し、終わったら元に戻す。Editor のプレビューはプロジェクト側の同名シェーダーへ差し替わることがあり、プレビューだけではアプリの見た目を保証できない。
- bundle の中身は Unity 依存のため、将来別フォーマット（glTF/VRM 等）へ移行する可能性がある。
- VRM（`.vrm`）は `.vrcaster` を経由せず Runtime が直接読み込む（`VrmLoader`）。表情・まぶた・リップシンク・SpringBone は
  読込時に上記 metadata と同じ型（`ExpressionSet` / `AvatarDescriptorData` / `PhysBoneSet`）へ変換し、同じ `Validate` を通す。
