using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// アタッチ先モデルの選択行 (なし / 他の配置モデル)。操作ウィンドウと Inspector で共用する。
    /// コンボボックスの開閉状態を持つため、描く場所ごとにインスタンスを分ける
    /// </summary>
    public class ModelParentRowDrawer
    {
        private static SelfModelPlacer placer => SelfModelPlacer.instance;

        /// <summary>アタッチ先の選択肢 (先頭の null は「なし」)</summary>
        private readonly List<StudioModelStatWrapper> _candidates = new List<StudioModelStatWrapper>();

        private readonly GUIComboBox<StudioModelStatWrapper> _comboBox;

        public ModelParentRowDrawer(float buttonWidth)
        {
            _comboBox = new GUIComboBox<StudioModelStatWrapper>
            {
                items = _candidates,
                getName = (model, _) => model == null ? "なし" : model.displayName,
                buttonSize = new Vector2(buttonWidth, 20),
            };
        }

        public void Draw(GUIView view, StudioModelStatWrapper model, string label, float labelWidth, float rowHeight,
            GUIStyle labelStyle = null)
        {
            _candidates.Clear();
            _candidates.Add(null);
            foreach (var candidate in placer.modelList)
            {
                if (placer.CanAttachToModel(model, candidate))
                {
                    _candidates.Add(candidate);
                }
            }

            var parent = placer.GetParentModel(model);

            view.BeginHorizontal();
            {
                view.DrawLabel(label, labelWidth, rowHeight, style: labelStyle);

                _comboBox.currentIndex = Mathf.Max(0, _candidates.IndexOf(parent));
                _comboBox.onSelected = (selected, _) =>
                {
                    if (selected != null)
                    {
                        placer.AttachToModelFromUI(model, selected);
                    }
                    else if (placer.GetParentModel(model) != null)
                    {
                        // 「なし」はモデルへのアタッチの解除だけ。メイドへのアタッチは上の行で変える
                        placer.AttachFromUI(model, null, SelfModelPlacer.AttachPoints[0]);
                    }
                };
                _comboBox.DrawButton(view);
            }
            view.EndLayout();
        }
    }
}
