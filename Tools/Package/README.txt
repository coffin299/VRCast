VRCast
======

[日本語]

VRChat のアバターを単体で表示し、OBS などの配信ソフトや Discord / Zoom に映すアプリです。

■ はじめかた
1. このフォルダの VRCast.exe を起動します（フォルダごと好きな場所に置けます）。
2. 左のパネルの「はじめに」タブが、アバターの読み込みから OBS に映すまでを案内します。
3. アバターは Unity で .vrcaster ファイルに書き出してから、VRCast のウィンドウにドラッグ＆ドロップします。

■ 書き出しツールの導入（アバターの Unity プロジェクト側）
1. VCC からアバターの入ったプロジェクトを開きます。
2. このフォルダの VRCast-Converter.unitypackage をダブルクリック（または Unity にドラッグ＆ドロップ）して「Import」を押します。
3. メニュー「VRCast」→「Avatar Exporter」で、シーン上のアバターを指定して「Export...」で .vrcaster を保存します。
元のアバターは変更しません。不要になったら Assets/VRCast/Converter フォルダを削除してください。

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
同梱しているサードパーティのライセンス表記は NOTICE.txt をご覧ください。
https://github.com/coffin299/VRCast


[English]

VRCast shows a VRChat avatar on its own and sends it to streaming software such as OBS, or to Discord / Zoom.

- Getting started
1. Run VRCast.exe in this folder (you can put the folder anywhere).
2. The "Start" tab in the left panel guides you from loading your avatar to showing it in OBS.
3. Export your avatar to a .vrcaster file in Unity, then drag and drop it onto the VRCast window.

- Installing the exporter (in your avatar's Unity project)
1. Open the project with your avatar from VCC.
2. Double-click VRCast-Converter.unitypackage in this folder (or drag it into Unity) and click "Import".
3. Open "VRCast" > "Avatar Exporter", pick your avatar in the scene and click "Export..." to save a .vrcaster file.
Your original avatar is never modified. To remove it, delete the Assets/VRCast/Converter folder.

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
See NOTICE.txt for the notices of bundled third-party software.
https://github.com/coffin299/VRCast
