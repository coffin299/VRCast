# パーフェクトシンク作成モード — 設計

ARKit 名の BlendShape を持たないアバターでも、VRCast の中で形状を作ってパーフェクトシンクを使えるようにする。
作った形状は `.vrcaster` の metadata に追記し、**既存のパッケージ形式（formatVersion 0）との互換を保つ**。

## 目標と範囲

- 操作パネルを「作成モード」へ擬似的に切り替え（シーンは切り替えない）、顔のアップのカメラで編集する。
- ARKit 互換 51 種（`ArKitFace.BlendShapeNames`）ごとに、頂点の移動量（BlendShape の差分）を作る。
  - 下地: アバターの既存 BlendShape を重み付きで足し込む。
  - 頂点ブラシ: つまむ（移動）・膨らませる / へこませる・なめらかにする・元に戻す。半径・強さ・左右対称。
  - 左右反転コピー（`mouthSmileLeft` → `mouthSmileRight` 等）、クリア、元に戻す / やり直し。
  - 重みのスライダーでの確認と、トラッキング（ARKit の値を送る入力元）でのプレビュー。
- `.vrcaster` へ保存（元のファイルは初回だけ `.bak` として残す）。別の `.vrcaster` から読み込むこともできる。
- 対象外: アバターが元から持っている ARKit 名の形状の上書き（同名の BlendShape は追加できないため、一覧では「既存」として編集不可）。

## 前提: 読み取り可能なメッシュ

実行時に頂点の位置・既存 BlendShape を読むには、bundle 内のメッシュが Read/Write 有効である必要がある
（FBX の既定は無効で、AssetBundle の中では読めない）。

- Converter の書き出しオプション「顔メッシュを編集可能にする（パーフェクトシンク）」（既定 ON、EditorPrefs に保存）。
  書き出し用の複製で、次の SkinnedMeshRenderer のメッシュを読み取り可能な複製（一時フォルダのアセット）へ差し替える。
  - Descriptor のリップシンク・まぶた用のメッシュ
  - Humanoid の Head（またはその子孫）のボーンを使い、BlendShape を持つか、名前に歯・舌・口（teeth / tooth / tongue / mouth / 歯 / 舌 / 口）を含むメッシュ
  - 元から読み取り可能なメッシュは差し替えない
- 読み取り可能なメッシュは CPU 側にもデータを持つため、そのメッシュの分だけメモリが増える。
- 旧バージョンの書き出しツールで作ったパッケージでは作成モードを使えない（読み込み・表示は従来どおり）。書き出し直しを案内する。

## パッケージ形式（追加分）

旧バージョンの Runtime は `metadata/` 直下の `*.json` を名前を問わず許可し、知らないものは読まない。
そこで形状は次の名前で追加する（`formatVersion` は 0 のまま）。

```text
metadata/perfectsync.json                 目次（メッシュの照合情報と形状名の一覧）
metadata/perfectsync_<ARKit 名>.json      形状ごとの差分（1 ファイル 1 MiB 以下）
```

- 旧 Runtime: 未知の metadata として無視する（アバターは従来どおり表示され、作った形状だけ使われない）。
- 新 Runtime + 旧パッケージ: ファイルが無いので従来どおり。
- `avatar.bundle` は変更しないため、`bundleSha256` の検証・展開キャッシュはそのまま使える。
- エントリ数（256）・全体サイズ（1 GiB）の上限は旧 Runtime と共通。形状は最大 51 + 目次 1。

### metadata/perfectsync.json

```json
{
    "version": 1,
    "meshes": [
        { "path": "Body", "vertexCount": 24567, "vertexHash": "0123456789abcdef" }
    ],
    "shapes": ["mouthSmileLeft", "mouthSmileRight"]
}
```

| フィールド | 内容 |
| :--- | :--- |
| `version` | 1（この節の形式。上げたら旧 Runtime は無視する） |
| `meshes[].path` | SkinnedMeshRenderer のアバタールートからの相対パス（最大 16） |
| `meshes[].vertexCount` | 保存時の頂点数（1〜1,000,000）。一致しないメッシュの差分は使わない |
| `meshes[].vertexHash` | 保存時の頂点位置（0.1 mm 単位に丸め）の FNV-1a 64 bit（小文字 16 進 16 桁）。書き出し直しでメッシュが変わったことを検出する |
| `shapes` | 形状名（英字で始まる英数字 64 文字以内、重複なし、最大 64） |

### metadata/perfectsync_<名前>.json

```json
{
    "name": "mouthSmileLeft",
    "meshes": [
        { "mesh": 0, "indices": "base64", "deltas": "base64" }
    ]
}
```

| フィールド | 内容 |
| :--- | :--- |
| `name` | 目次の `shapes` の名前と一致 |
| `meshes[].mesh` | 目次の `meshes` の位置 |
| `meshes[].indices` | 動かす頂点番号（int32 リトルエンディアン、昇順・重複なし）の base64 |
| `meshes[].deltas` | 頂点ごとの位置の差分（メッシュ空間、half float の x, y, z）の base64。各成分 ±10 m 以内 |

法線の差分は保存しない（読み込み時に、変形前後の法線の再計算結果の差から作る）。

不正な目次・形状ファイルは警告して無視する（アバター表示は続ける）。

## Runtime の構成

| 部品 | 役割 |
| :--- | :--- |
| `AvatarFormat.PerfectSyncSet` / `PerfectSyncShape` / `PerfectSyncCodec` | 形式・検証・base64 の変換（Converter と共有） |
| `Avatars.AvatarPackageReader` | 目次と形状を読み込む。`ReadPerfectSync` は bundle を展開せずに読む（別のパッケージからの読み込み用） |
| `Avatars.AvatarPackageWriter` | `.vrcaster` の perfectsync エントリだけを差し替える（一時ファイル → `.bak` → 置き換え） |
| `PerfectSync.EditableFaceMesh` | 1 メッシュ分: 元のメッシュ、作業用の複製、形状ごとの差分（頂点数ぶんの配列）、再構築 |
| `PerfectSync.CustomPerfectSync` | 読み込み時に保存済みの形状を照合して追加。再構築したら通知（上限・パーフェクトシンクの対象を作り直す） |
| `PerfectSync.PerfectSyncSculptor` | 編集モード中の処理: 顔の固定、ブラシ、カーソル、元に戻す |
| `PerfectSync.MirrorMap` | 左右対称の頂点の対応（メッシュ空間で対称面を自動判定） |
| `UI.PerfectSyncEditorSection` | 作成モードの操作パネル |

### 形状の追加と再構築

- 作業用メッシュ = `Instantiate(元のメッシュ)` に、差分のある形状を ARKit の並び順で `AddBlendShapeFrame`（重み 100）。
  元の BlendShape の番号は変わらないので、表情・まばたき等の参照（レンダラー + 番号）はそのまま使える。
- メッシュを差し替えると BlendShape の重みが消えるため、元の BlendShape の重みを写し直す。
- 再構築後に `BlendShapeLimiter.Rescan`（一覧を作り直し、保存済みの上限を当て直す）と
  `FaceTrackingDriver.RefreshPerfectSync`（ARKit 名の対象を探し直す）を呼ぶ。

### 編集中の表示

- 編集中の形状は BlendShape ではなく、作業用メッシュの頂点（元の位置 + 差分 × 重み）で表示する（毎回再構築しない）。
  形状の切り替え・プレビュー・保存・終了のときだけ再構築する。
- 編集中は顔のトラッキングを止め（`FaceTrackingDriver.Suspended`）、編集対象メッシュの BlendShape を読み込み時の値に固定する
  （まばたき・口パク・表情で形が変わらないように）。
- 「トラッキングでプレビュー」中は固定をやめ、パーフェクトシンクで動かす（ブラシは使えない）。

### ブラシ

- マウス位置の光線と、`BakeMesh` した現在の形の三角形（Head 以下のボーンを使う頂点だけ）との交点を求める（物理エンジンは使わない）。
- 範囲内の頂点（交点からの距離 < 半径、影響度 `(1 - (d/r)²)²`）を、交点のあるメッシュだけ動かす。
- ワールドでの移動量は、頂点ごとのスキニング行列（ボーン行列 × バインドポーズの重み付き和）の逆行列でメッシュ空間へ戻す。
- 種類:
  - つまむ: 押した位置を通る画面に平行な面の上で、マウスの移動量だけ動かす（押した時点の範囲・影響度で固定）
  - 膨らませる / へこませる: 元の法線方向へ押し出す（押している間、毎フレーム）
  - なめらかにする: 差分を隣接頂点の平均へ寄せる
  - 元に戻す: 差分を 0 へ寄せる
- 左右対称: 変化量を対称の頂点へ反転して加える（両側が範囲内なら平均）。
- 元に戻す / やり直し: 操作の前の差分（そのメッシュの形状 1 つ分）を最大 30 段保存する。

### 左右対称の対応

メッシュ空間の X / Y / Z 軸それぞれについて、原点と境界の中央を対称面の候補にし、
頂点位置を反転した先に頂点がある（境界の対角線の 1/10000 以内）数が最も多い面を使う。

## UI

- 入口: 「はじめに」の下の「パーフェクトシンク設定(BETA)」タブと、「トラッキング」タブのパーフェクトシンクの項目（どちらも「パーフェクトシンクの形状を作る...」ボタン）。
- 作成モード中はタブ列を隠し、作成モードの操作だけを出す（擬似的な画面切り替え）。カメラは顔のアップにし、終了時に元へ戻す。
- 操作: 左ドラッグ = ブラシ、右ドラッグ = 回転、中ドラッグ = 移動、ホイール = ズーム（従来どおり）、
  Ctrl+Z / Ctrl+Y = 元に戻す / やり直し、`[` / `]` = 半径。
- 未保存のまま終了するときは確認する（終了ボタンは 2 段階: 保存して終了 / 保存せずに終了 / キャンセル）。
  アバターを切り替える（読み込み・ドロップ）と作成モードは確認なしで閉じ、未保存の形状は破棄される。
- 作成中は視点の記録（アバターごとのカメラ）を止め、「カメラを固定」を無視する。

## テスト

- `PerfectSyncCodec`: 往復、壊れた base64・数の不一致・範囲外の拒否
- `PerfectSyncSet` / `PerfectSyncShape` の検証
- `AvatarPackageReader`: 形状の読み込み、不正な形状の無視、`IsAllowedEntryName` が新しい名前を許可すること（旧 Runtime と同じ規則）
- `AvatarPackageWriter`: 追記後も読めること、他のエントリ・bundle が変わらないこと、`.bak` の作成、差し替え・削除
- `MirrorMap`: 対称なメッシュで対応が見つかること、軸の自動判定
