using System.IO;
using UnityEngine;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// 「パーフェクトシンク化」タブ。表示中アバターの対応状況を示し、編集画面（PerfectSyncEditor）を開く。
    /// </summary>
    public class PerfectSyncSection
    {
        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly PerfectSyncEditor _editor;

        // 頂点を読めるメッシュの数（アバターが替わったときだけ数え直す）
        private int _readableMeshes;

        public PerfectSyncSection(AvatarSession session, PerfectSyncEditor editor)
        {
            _session = session;
            _avatar = new AvatarComponentCache(session);
            _editor = editor;
        }

        public void Draw()
        {
            // アバターが替わったら読めるメッシュを数え直す
            LoadedAvatar avatar = _session.Current;
            if (_avatar.Refresh())
            {
                _readableMeshes = avatar != null && avatar.Instance != null
                    ? PerfectSyncEditor.CountReadableMeshes(avatar.Instance)
                    : 0;
            }

            GuiControls.BeginCard(Loc.T("Perfect sync setup", "パーフェクトシンク化", "퍼펙트 싱크화", "完美同步化", "完美同步化"));
            GuiControls.Hint(Loc.T(
                "Place points on the avatar's face to generate the ARKit face shapes (the 52 used by VRCast) automatically, " +
                "and assign blend shapes the avatar already has. The result is saved as a new .vrcaster file; " +
                "the original file is not changed.",
                "アバターの顔に目印を打つと、ARKit の顔の形状（VRCast が使う 52 種）を自動で作り、" +
                "アバターに元からある BlendShape も割り当てます。結果は別名の .vrcaster として保存し、元のファイルは変更しません。",
                "아바타 얼굴에 표시를 찍으면 ARKit 얼굴 형상(VRCast가 쓰는 52종)을 자동으로 만들고, " +
                "아바타에 원래 있는 BlendShape도 할당합니다. 결과는 다른 이름의 .vrcaster로 저장하며 원본 파일은 변경하지 않습니다.",
                "在虚拟形象脸上放置标记后，会自动生成 ARKit 面部形状（VRCast 使用的 52 种），" +
                "并分配形象原有的 BlendShape。结果另存为新的 .vrcaster 文件，不会修改原文件。",
                "在虛擬形象臉上放置標記後，會自動生成 ARKit 臉部形狀（VRCast 使用的 52 種），" +
                "並分配形象原有的 BlendShape。結果另存為新的 .vrcaster 檔案，不會修改原檔案。"));
            GuiControls.Warning(PerfectSyncLabels.MissingPartsCaution());

            // 始められない理由（未読み込み・VRM・書き出し直しが必要）
            string reason = PerfectSyncEditor.CanEdit(avatar);
            if (reason != null)
            {
                GuiControls.Warning(reason);
                GuiControls.EndCard();
                return;
            }

            // 今の対応状況（使える ARKit の形状の数）
            var driver = _avatar.Get<FaceTrackingDriver>();
            int matched = driver != null ? driver.PerfectSyncShapeCount : 0;
            int total = ArKitFace.BlendShapeNames.Length;
            GuiControls.Status(Loc.T($"ARKit shapes now: {matched}/{total}", $"今使える ARKit の形状: {matched}/{total}",
                $"현재 사용 가능한 ARKit 형상: {matched}/{total}", $"当前可用的 ARKit 形状: {matched}/{total}",
                $"目前可用的 ARKit 形狀: {matched}/{total}"), matched >= PerfectSyncBlendShapes.MinMatchedShapes);
            GuiControls.Hint(Loc.T($"Editable meshes: {_readableMeshes}", $"編集できるメッシュ: {_readableMeshes} 個",
                $"편집 가능한 메시: {_readableMeshes}개", $"可编辑的网格: {_readableMeshes} 个", $"可編輯的網格: {_readableMeshes} 個"));

            // パーフェクトシンク版のファイルなら、元のファイル名と、続きから編集できることを示す
            if (!avatar.PerfectSync.IsEmpty)
            {
                string source = string.IsNullOrEmpty(avatar.PerfectSync.sourceName)
                    ? Path.GetFileName(avatar.SourcePath)
                    : avatar.PerfectSync.sourceName;
                GuiControls.Status(Loc.T(
                    $"This is a perfect sync version (made from {source}). Editing continues from the saved points.",
                    $"パーフェクトシンク版です（元: {source}）。保存した目印の続きから編集できます。",
                    $"퍼펙트 싱크 버전입니다 (원본: {source}). 저장한 표시에서 이어서 편집할 수 있습니다.",
                    $"这是完美同步版本（原文件: {source}）。可从保存的标记继续编辑。",
                    $"這是完美同步版本（原檔案: {source}）。可從儲存的標記繼續編輯。"), true);
            }

            // 編集画面を開く
            UiTheme theme = UiTheme.Current;
            if (GUILayout.Button(Loc.T("Start editing", "編集を始める", "편집 시작", "开始编辑", "開始編輯"),
                    theme != null ? theme.AccentButton : GUI.skin.button, GUILayout.ExpandWidth(true)))
            {
                _editor.Open(avatar);
            }

            GuiControls.EndCard();
        }
    }
}
