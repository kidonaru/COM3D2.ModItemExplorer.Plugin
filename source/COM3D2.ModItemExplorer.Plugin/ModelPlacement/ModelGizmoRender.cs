using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 掴み判定を自分で制御する GizmoRender。
    /// GizmoRender は掴み状態 beSelectedType をインスタンスごとに持ち、かつ軸線・スケールのハンドル判定は
    /// control_lock も NGUI のヒット判定も見ないため、放っておくとドラッグ中にカーソルが重なった
    /// ギズモへ次々と操作が伝播してしまう。
    /// そこで「掴めるのは押下フレームだけ」「掴んだ 1 個だけが操作を継続できる」という 2 点に絞り、
    /// 対象外のインスタンスは押下フラグ is_drag_ を倒した状態で base を呼んで判定だけ飛ばす
    /// （描画はそのまま行われる）
    /// </summary>
    public class ModelGizmoRender : GizmoRender
    {
        private static ModelGizmoRender _dragOwner = null;

        public override void OnRenderObject()
        {
            if (!GizmoRenderHack.isAvailable)
            {
                // 判定できないなら多重操作の防止を諦め、ゲーム標準の挙動に任せる
                base.OnRenderObject();
                return;
            }

            // 破棄・無効化された所有者を掴んだままにしないよう毎フレーム掃除する。
            // Unity の == は破棄済みオブジェクトも null 扱いにするので両方ここで拾える
            if (_dragOwner == null || !_dragOwner.isActiveAndEnabled)
            {
                _dragOwner = null;
            }

            if (!CanInteract())
            {
                RenderWithoutGrab();
                return;
            }

            base.OnRenderObject();

            if (GizmoRenderHack.IsGrabbed(this))
            {
                _dragOwner = this;
            }
            else if (_dragOwner == this)
            {
                _dragOwner = null;
            }
        }

        /// <summary>
        /// ハンドル判定を許すかどうか。
        /// ドラッグ中の所有者だけが操作を継続でき、新たに掴めるのは所有者がいない押下フレームだけ。
        /// これで「押下開始地点がハンドル外なら操作しない」と「ドラッグ中に他のギズモを巻き込まない」を
        /// まとめて満たせる
        /// </summary>
        private bool CanInteract()
        {
            if (_dragOwner == this)
            {
                return true;
            }

            return _dragOwner == null && Input.GetMouseButtonDown(0);
        }

        /// <summary>
        /// is_drag_ を倒した状態で base を呼び、ハンドル判定を飛ばして描画だけさせる。
        /// 掴み状態が残っていると base 側が所有者のロック（local_control_lock_）まで落としてしまうため、
        /// 呼ぶ前に念のため解除しておく（無効化中に掴んだまま止まった場合などに残りうる）
        /// </summary>
        private void RenderWithoutGrab()
        {
            GizmoRenderHack.ClearGrabbed(this);

            var prevIsDrag = GizmoRenderHack.isDrag;
            GizmoRenderHack.isDrag = false;
            try
            {
                base.OnRenderObject();
            }
            finally
            {
                GizmoRenderHack.isDrag = prevIsDrag;
            }
        }
    }
}
