VRCast
======

[日本語]

VRChat のアバターを単体で表示し、OBS などの配信ソフトや Discord / Zoom に映すアプリです。

■ はじめかた
1. このフォルダの VRCast.exe を起動します（フォルダごと好きな場所に置けます）。
2. 左のパネルの「はじめに」タブが、アバターの読み込みから OBS に映すまでを案内します。
3. アバターは Unity で .vrcaster ファイルに書き出してから、VRCast のウィンドウにドラッグ＆ドロップします。
4. Web カメラのトラッキング（MediaPipe）では、笑顔・驚き・怒り・悲しみがアバターの表情に反映されます。
   「トラッキング」タブの「表情を反映」で ON/OFF・しきい値・割り当てを変えられます。
表示言語はパネル上部のボタン（または「設定」タブ）で 英語 / 日本語 / 韓国語 / 中国語（簡体字・繁体字）から選べます。
ゲームや OBS と同時に使うときの負荷を減らす「軽量モード」が最初から ON になっています。
動きの滑らかさを優先したいときは「設定」タブで OFF にしてください。
起動時に新しいバージョンがあるかを確認し、あればパネル上部でお知らせします
（最新のバージョン番号を読むためだけに Web サイトへ接続します。「設定」タブで OFF にできます）。

■ 書き出しツールの導入（アバターの Unity プロジェクト側）
1. VCC からアバターの入ったプロジェクトを開きます。
2. このフォルダの VRCast-Converter.unitypackage をダブルクリック（または Unity にドラッグ＆ドロップ）して「Import」を押します。
3. メニュー「VRCast」→「Avatar Exporter」で、シーン上のアバターを指定して「Export...」で .vrcaster を保存します。
元のアバターは変更しません。Modular Avatar で改変したアバターも、そのまま指定すれば改変後の姿で書き出されます（ベータ版）。
MA で付けた衣装・小物は、アバターのボーンに付け替えて体に追従させます。
不要になったら Assets/VRCast/Converter フォルダを削除してください。

■ ヘルプ
https://coffin299.github.io/VRCast/help/

■ 更新履歴
CHANGELOG.txt をご覧ください。

■ ご注意
- 非公式ツールです。VRChat Inc. とは関係ありません。
- アバター・衣装の利用規約を守ってお使いください。
- 仮想カメラを使った場合は、このフォルダを移動・削除する前に「出力」タブの「ドライバーを解除」を押してください。
- 設定とログ: %USERPROFILE%\AppData\LocalLow\VRCast\VRCast\
  不具合の報告時は Player.log を添えてください: https://github.com/coffin299/VRCast/issues

■ ライセンス
VRCast は Apache License 2.0 です（LICENSE.txt）。
同梱しているサードパーティのライセンス表記は NOTICE.txt をご覧ください（アプリの「クレジット」タブから GitHub でも開けます）。
開発者: ごみぃ（https://x.com/coffin299） / 協力者: Arche_039（https://x.com/Arche_039）
https://github.com/coffin299/VRCast


[English]

VRCast shows a VRChat avatar on its own and sends it to streaming software such as OBS, or to Discord / Zoom.

- Getting started
1. Run VRCast.exe in this folder (you can put the folder anywhere).
2. The "Start" tab in the left panel guides you from loading your avatar to showing it in OBS.
3. Export your avatar to a .vrcaster file in Unity, then drag and drop it onto the VRCast window.
4. With webcam tracking (MediaPipe), your smile, surprise, anger and sadness switch the avatar's expressions.
   Use "Facial expressions" in the Tracking tab to turn it on/off and change sensitivity and mapping.
The panel language can be English, Japanese, Korean or Chinese (Simplified / Traditional) (buttons at the top of the panel, or the Settings tab).
"Low load mode", which reduces load when running with games or OBS, is on by default.
Turn it off in the Settings tab if you prefer smoother motion.
VRCast checks for a new version at startup and shows a notice at the top of the panel if there is one
(it connects to the website only to read the latest version number; you can turn this off in the Settings tab).

- Installing the exporter (in your avatar's Unity project)
1. Open the project with your avatar from VCC.
2. Double-click VRCast-Converter.unitypackage in this folder (or drag it into Unity) and click "Import".
3. Open "VRCast" > "Avatar Exporter", pick your avatar in the scene and click "Export..." to save a .vrcaster file.
Your original avatar is never modified. Avatars customized with Modular Avatar are exported with the changes applied (beta).
Outfits and accessories added with MA are attached to the avatar's bones so they follow the body.
To remove it, delete the Assets/VRCast/Converter folder.

- Help
https://coffin299.github.io/VRCast/help/

- Changelog
See CHANGELOG.txt.

- Notes
- This is an unofficial tool and is not affiliated with VRChat Inc.
- Please follow the terms of use of your avatar and outfits.
- If you used the virtual camera, press "Uninstall driver" in the Output tab before moving or deleting this folder.
- Settings and logs: %USERPROFILE%\AppData\LocalLow\VRCast\VRCast\
  Please attach Player.log when reporting a problem: https://github.com/coffin299/VRCast/issues

- License
VRCast is licensed under the Apache License 2.0 (LICENSE.txt).
See NOTICE.txt for the notices of bundled third-party software (also available on GitHub from the Credits tab).
Developer: ごみぃ (https://x.com/coffin299) / Collaborator: Arche_039 (https://x.com/Arche_039)
https://github.com/coffin299/VRCast
