using System;
using System.Reflection;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// GizmoRender の private な内部状態へアクセスするためのリフレクション置き場。
    /// ハンドルの掴み判定を外から制御するにはこれらのフラグを触るしかないため、
    /// ゲーム更新で名前が変わったときに 1 箇所で気付けるよう集約している
    /// </summary>
    public static class GizmoRenderHack
    {
        private const string MoveTypeNoneName = "NONE";

        /// <summary>
        /// GizmoRender がマウス押下を掴んでいるかを表す private static フラグ
        /// </summary>
        private static readonly FieldInfo _isDragField = typeof(GizmoRender)
            .GetField("is_drag_", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>
        /// どのハンドルを掴んでいるかを表す private なインスタンスフィールド
        /// </summary>
        private static readonly FieldInfo _beSelectedTypeField = typeof(GizmoRender)
            .GetField("beSelectedType", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// 掴んでいない状態を表す MOVETYPE.NONE。private な入れ子 enum なので実値もリフレクションで引く
        /// </summary>
        private static readonly object _moveTypeNone = ParseMoveTypeNone();

        /// <summary>
        /// 内部状態を読み書きできるか。false ならゲーム標準の挙動に任せるしかない
        /// </summary>
        public static bool isAvailable
        {
            get { return _isDragField != null && _beSelectedTypeField != null && _moveTypeNone != null; }
        }

        /// <summary>
        /// 押下フラグ。倒すと OnRenderObject がハンドル判定前に抜けるため、掴みだけを止められる。
        /// GizmoRender.Update は押下・解放のフレームでしか書き換えないので、倒すのは押下フレームの 1 回でよい
        /// </summary>
        public static bool isDrag
        {
            get { return _isDragField != null && (bool)_isDragField.GetValue(null); }
            set
            {
                if (_isDragField != null)
                {
                    _isDragField.SetValue(null, value);
                }
            }
        }

        static GizmoRenderHack()
        {
            // ゲーム更新で名前が変わるとギズモの横取り防止が無言で効かなくなるため検知できるようにする
            MTEUtils.AssertNull(_isDragField != null, "_isDragField is null");
            MTEUtils.AssertNull(_beSelectedTypeField != null, "_beSelectedTypeField is null");
            MTEUtils.AssertNull(_moveTypeNone != null, "_moveTypeNone is null");
        }

        public static bool IsGrabbed(GizmoRender gizmo)
        {
            if (!isAvailable)
            {
                return false;
            }

            return !_moveTypeNone.Equals(_beSelectedTypeField.GetValue(gizmo));
        }

        public static void ClearGrabbed(GizmoRender gizmo)
        {
            if (!isAvailable)
            {
                return;
            }

            _beSelectedTypeField.SetValue(gizmo, _moveTypeNone);
        }

        private static object ParseMoveTypeNone()
        {
            if (_beSelectedTypeField == null)
            {
                return null;
            }

            var fieldType = _beSelectedTypeField.FieldType;
            if (!fieldType.IsEnum || !Enum.IsDefined(fieldType, MoveTypeNoneName))
            {
                return null;
            }

            return Enum.Parse(fieldType, MoveTypeNoneName);
        }
    }
}
