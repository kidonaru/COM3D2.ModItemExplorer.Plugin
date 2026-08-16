using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 配置モデルの TransformGizmo を一括管理する。
    /// SceneEditor の GizmoHost が使える環境では登録して SceneView / GameView の
    /// 入力・描画ディスパッチに乗り、稼働ビューが無い間は standalone (Camera.main +
    /// Input.mousePosition) で自前駆動する。GizmoHost は後からロードされる可能性が
    /// あるため、解決できるまで一定間隔で再試行する
    /// (Update は毎フレーム呼んでよい。InputRemapperClient と同じパターン)
    /// </summary>
    public class ModelGizmoManager
    {
        private static ModelGizmoManager _instance;
        public static ModelGizmoManager instance
            => _instance ?? (_instance = new ModelGizmoManager());

        private readonly Dictionary<GameObject, TransformGizmo> _gizmos
            = new Dictionary<GameObject, TransformGizmo>();

        private GizmoTool _tool = GizmoTool.Move;
        private bool _useLocalSpace = true;
        private bool _visible;

        // ギズモの表示対象を選択中のモデルだけに絞るか、と、その選択中モデル
        private bool _targetSelectedOnly;
        private GameObject _selectedTarget;

        // standalone 描画用にメインカメラへ付けるフック
        private StandaloneDrawHook _drawHook;

        /// <summary>
        /// ホスト経由で駆動中か。登録済みでもホストのビューが稼働していなければ
        /// 描画も入力も届かないため、その間は standalone へ落とす
        /// </summary>
        private bool isHosted => _hostHandle != null && GizmoHostClient.isViewActive;

        /// <summary>
        /// プラグインが有効か。ホスト経由の描画・掴みは Update とは独立に呼ばれるため、
        /// 無効化中にギズモが残らないよう各入口で確認する
        /// </summary>
        private static bool isPluginEnabled
        {
            get
            {
                var plugin = ModItemExplorer.instance;
                return plugin != null && plugin.isEnable;
            }
        }

        public bool isDragging { get; private set; }
        private TransformGizmo _dragGizmo;

        // ドラッグを開始した経路 (hosted / standalone)。途中で経路が切り替わると
        // 座標の基準カメラが変わって対象が飛ぶため、切替を検知したら打ち切る
        private bool _dragHosted;

        public void AddGizmo(GameObject target)
        {
            if (target == null || _gizmos.ContainsKey(target))
            {
                return;
            }
            var gizmo = new TransformGizmo
            {
                target = target.transform,
                tool = GetAppliedTool(target),
                sizeScale = SelfModelPlacer.GizmoScale,
                useLocalSpace = _useLocalSpace,
            };
            _gizmos[target] = gizmo;
        }

        public void RemoveGizmo(GameObject target)
        {
            if (target == null)
            {
                return;
            }
            TransformGizmo gizmo;
            if (_gizmos.TryGetValue(target, out gizmo))
            {
                if (_dragGizmo == gizmo)
                {
                    EndDrag();
                }
                _gizmos.Remove(target);
            }
        }

        /// <summary>操作種別と表示状態をまとめて反映する。非表示は tool = None で表現する</summary>
        public void SetToolAndVisible(GizmoTool tool, bool visible)
        {
            _tool = tool;
            _visible = visible;
            ApplyTool();
        }

        /// <summary>
        /// ギズモを表示する対象を絞る。selectedOnly が false なら全モデルに表示する。
        /// selectedOnly かつ target が null のときはどのモデルにも表示しない
        /// </summary>
        public void SetVisibleTarget(bool selectedOnly, GameObject target)
        {
            _targetSelectedOnly = selectedOnly;
            _selectedTarget = target;
            ApplyTool();
        }

        /// <summary>
        /// 対象に適用する操作種別。表示条件を満たさないものは None（＝非表示）にする
        /// </summary>
        private GizmoTool GetAppliedTool(GameObject target)
        {
            if (!_visible)
            {
                return GizmoTool.None;
            }
            if (_targetSelectedOnly && target != _selectedTarget)
            {
                return GizmoTool.None;
            }
            return _tool;
        }

        private void ApplyTool()
        {
            foreach (var pair in _gizmos)
            {
                pair.Value.tool = GetAppliedTool(pair.Key);
            }
        }

        /// <summary>軸空間 (Local/Global) を全ギズモへ反映する</summary>
        public void SetUseLocalSpace(bool useLocalSpace)
        {
            _useLocalSpace = useLocalSpace;
            foreach (var gizmo in _gizmos.Values)
            {
                gizmo.useLocalSpace = useLocalSpace;
            }
        }

        private readonly List<GameObject> _removeBuffer = new List<GameObject>();

        public void Update()
        {
            TryRegisterHost();

            var hosted = isHosted;

            // 経路が切り替わると、開始側は更新も解放も呼ばれず isDragging が残り、
            // 受け側は別カメラの座標で解決してしまうため、ここで打ち切る
            if (isDragging && hosted != _dragHosted)
            {
                EndDrag();
            }

            if (hosted)
            {
                // ホストが両ビューで描画するため standalone のフックは外す
                DetachDrawHook();
            }
            else
            {
                UpdateStandaloneInput();
            }

            // 破棄済みターゲットの掃除 (Unity の == は破棄済みも null 扱い)
            _removeBuffer.Clear();
            foreach (var pair in _gizmos)
            {
                if (pair.Key == null)
                {
                    _removeBuffer.Add(pair.Key);
                }
            }
            foreach (var key in _removeBuffer)
            {
                RemoveGizmo(key);
            }
        }

        // ---- GizmoHost 連携 ----

        private object _hostHandle;
        // 再試行間隔 (フレーム)。ホスト型の解決は毎フレーム行うほど安くはない
        private const int RETRY_INTERVAL_FRAMES = 60;
        // int.MinValue だと frame - _lastAttemptFrame がオーバーフローして負になり、
        // リトライガードが恒久的に成立して一度も解決を試行しなくなる
        private int _lastAttemptFrame = -RETRY_INTERVAL_FRAMES;
        private bool _hostResolved;

        private void TryRegisterHost()
        {
            if (_hostResolved)
            {
                return;
            }

            var frame = Time.frameCount;
            if (frame - _lastAttemptFrame < RETRY_INTERVAL_FRAMES)
            {
                return;
            }
            _lastAttemptFrame = frame;

            if (!GizmoHostClient.isAvailable)
            {
                return;
            }

            _hostResolved = true;
            _hostHandle = GizmoHostClient.Register(
                "ModItemExplorer",
                TryBeginDrag,
                // TransformGizmo は掴んだカメラを内部で保持し続けるため、更新時の camera は使わない
                (camera, rtPoint) => UpdateDrag(rtPoint),
                EndDrag,
                () => isDragging,
                DrawAll);

            if (_hostHandle != null)
            {
                MTEUtils.LogDebug("ModelGizmoManager: GizmoHost へ登録しました");
            }
        }

        // ---- 入力 (ホスト経由・standalone 共通のコア) ----

        private bool TryBeginDrag(Camera camera, Vector2 rtPoint)
        {
            // ホストと standalone が同フレームに掴みを試すことがあるため、二重掴みを弾く
            if (isDragging || !isPluginEnabled)
            {
                return false;
            }

            // 自プラグインのウィンドウ上からの押下では掴まない。ホスト側は自分のウィンドウしか
            // 追跡していないため、この抑止は standalone / hosted の両経路で自前に行う
            if (WindowManager.instance.isMouseOverWindow)
            {
                return false;
            }

            foreach (var gizmo in _gizmos.Values)
            {
                if (gizmo.tool == GizmoTool.None)
                {
                    continue;
                }
                if (gizmo.TryBeginDrag(camera, rtPoint))
                {
                    _dragGizmo = gizmo;
                    _dragHosted = isHosted;
                    isDragging = true;
                    return true;
                }
            }
            return false;
        }

        private void UpdateDrag(Vector2 rtPoint)
        {
            if (_dragGizmo == null)
            {
                return;
            }
            _dragGizmo.UpdateDrag(rtPoint);
        }

        private void EndDrag()
        {
            if (_dragGizmo != null)
            {
                _dragGizmo.EndDrag();
            }
            _dragGizmo = null;
            isDragging = false;
        }

        private void DrawAll(Camera camera)
        {
            // ホストや描画フックはプラグインの有効状態を知らないため、ここで止める
            if (!isPluginEnabled)
            {
                return;
            }

            foreach (var gizmo in _gizmos.Values)
            {
                gizmo.Draw(camera);
            }
        }

        // ---- standalone 駆動 ----

        private void UpdateStandaloneInput()
        {
            if (_gizmos.Count == 0)
            {
                DetachDrawHook();
                return;
            }
            AttachDrawHook();

            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            if (isDragging)
            {
                if (Input.GetMouseButton(0))
                {
                    // 旧バージョンの SceneEditor 環境では InputRemapper が GameView 内で
                    // RT 座標へ変換済みのため、Camera.main とのペアで正しく成立する
                    UpdateDrag((Vector2)Input.mousePosition);
                }
                else
                {
                    EndDrag();
                }
                return;
            }

            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            // 窓上の押下を除外する判定は TryBeginDrag が両経路で共通に行う
            TryBeginDrag(camera, (Vector2)Input.mousePosition);
        }

        private void AttachDrawHook()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }
            if (_drawHook != null && _drawHook.gameObject == camera.gameObject)
            {
                return;
            }
            DetachDrawHook();
            _drawHook = camera.gameObject.AddComponent<StandaloneDrawHook>();
            _drawHook.onPostRender = DrawAll;
        }

        private void DetachDrawHook()
        {
            if (_drawHook != null)
            {
                UnityEngine.Object.Destroy(_drawHook);
                _drawHook = null;
            }
        }

        /// <summary>standalone 時にメインカメラへ付ける描画フック</summary>
        private class StandaloneDrawHook : MonoBehaviour
        {
            public Action<Camera> onPostRender;
            private Camera _camera;

            private void Awake()
            {
                _camera = GetComponent<Camera>();
            }

            private void OnPostRender()
            {
                if (onPostRender != null && _camera != null)
                {
                    onPostRender(_camera);
                }
            }
        }
    }
}
