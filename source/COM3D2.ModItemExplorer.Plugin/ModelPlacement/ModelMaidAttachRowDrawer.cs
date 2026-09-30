using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 親メイドの選択行 (メイド + 部位)。操作ウィンドウと Inspector で共用する。
    /// コンボボックスの開閉状態を持つため、描く場所ごとにインスタンスを分ける
    /// </summary>
    public class ModelMaidAttachRowDrawer
    {
        private static SelfModelPlacer placer => SelfModelPlacer.instance;
        private static ModItemManager modItemManager => ModItemManager.instance;

        /// <summary>部位のボタン幅。部位名は 2 文字程度なので固定にし、残りはメイド側へ回す</summary>
        private const float PointButtonWidth = 60f;

        /// <summary>狭いウィンドウでもメイドのボタンが潰れて部位が押し出されないための下限</summary>
        private const float MinMaidButtonWidth = 40f;

        private readonly GUIComboBox<Maid> _maidComboBox = new GUIComboBox<Maid>
        {
            getName = (maid, _) => maid == null ? "なし" : maid.status.fullNameJpStyle,
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<SelfModelPlacer.AttachPoint> _pointComboBox
            = new GUIComboBox<SelfModelPlacer.AttachPoint>
        {
            items = SelfModelPlacer.AttachPoints,
            getName = (point, _) => point.displayName,
            buttonSize = new Vector2(PointButtonWidth, 20),
        };

        /// <summary>
        /// 部位が「なし」の間に最後に選んだメイド。アタッチ状態には現れないため、部位を選ぶまでここで覚える
        /// </summary>
        private Maid _pendingMaid;

        public void Draw(GUIView view, StudioModelStatWrapper model, string label, float labelWidth, float rowHeight,
            GUIStyle labelStyle = null)
        {
            var maids = MTEUtils.GetReadyMaidList();
            var attachedMaid = placer.GetAttachedMaid(model);

            // 非表示のメイドに付いたままでも、付け先を一覧に出して表示と実際の付け先をそろえる
            if (attachedMaid != null && !maids.Contains(attachedMaid))
            {
                maids.Add(attachedMaid);
            }
            _maidComboBox.items = maids.Count > 0 ? maids : new List<Maid> { null };

            var maid = attachedMaid;
            if (maid == null)
            {
                maid = maids.Contains(_pendingMaid) ? _pendingMaid : modItemManager.currentMaid;
            }

            view.BeginHorizontal();
            {
                view.DrawLabel(label, labelWidth, rowHeight, style: labelStyle);

                // 右端までの残り幅から、両コンボの前後送りボタン 4 個と部位のボタン、両コンボ間の余白 1 個を除く
                var remainingWidth = view.viewRect.width - view.padding.x * 2 - view.currentPos.x;
                var maidWidth = remainingWidth - PointButtonWidth - GUIComboBoxBase.ARROW_SIZE * 4 - view.margin;
                _maidComboBox.buttonSize = new Vector2(Mathf.Max(MinMaidButtonWidth, maidWidth), 20);

                _maidComboBox.currentIndex = Mathf.Max(0, _maidComboBox.items.IndexOf(maid));
                _maidComboBox.onSelected = (selected, _) =>
                {
                    _pendingMaid = selected;

                    // 付け替えは位置・回転をリセットするため、今と同じメイドを選び直しただけなら何もしない
                    var current = placer.GetAttachedMaid(model);
                    if (current == null || current == selected)
                    {
                        return;
                    }

                    // 同じ部位のままメイドだけ付け替える
                    var pointIndex = placer.GetAttachPointIndex(model);
                    placer.AttachFromUI(model, selected, SelfModelPlacer.AttachPoints[pointIndex]);
                };
                _maidComboBox.DrawButton(view);

                _pointComboBox.currentIndex = placer.GetAttachPointIndex(model);
                _pointComboBox.onSelected = (point, _) =>
                {
                    // 「なし」はメイドへのアタッチの解除だけ。モデルへのアタッチは下の行で変える
                    if (point.boneName == null && placer.GetParentModel(model) != null)
                    {
                        return;
                    }
                    placer.AttachFromUI(model, _maidComboBox.currentItem, point);
                };
                _pointComboBox.DrawButton(view);
            }
            view.EndLayout();
        }
    }
}
