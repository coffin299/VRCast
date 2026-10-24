using UnityEngine;

namespace VRCast.UI
{
    /// <summary>
    /// 画面上の案内を、操作パネルより手前に描く。
    /// GUILayout.Window は OnGUI の最後にまとめて描かれるため、その後の BeginArea はパネルの裏に回る。
    /// </summary>
    public static class FrontPrompt
    {
        /// <summary>
        /// rect の案内を、いま出ているウィンドウより手前に描く（style が背景）。
        /// </summary>
        public static void Draw(int id, Rect rect, GUIStyle style, GUI.WindowFunction content)
        {
            // 深度が小さいほど手前。パネルのウィンドウ（深度 0）より前に積む
            int depth = GUI.depth;
            GUI.depth = depth - 20;
            GUILayout.Window(id, rect, content, GUIContent.none, style,
                GUILayout.Width(rect.width), GUILayout.Height(rect.height));
            GUI.BringWindowToFront(id);
            GUI.depth = depth;
        }
    }
}
