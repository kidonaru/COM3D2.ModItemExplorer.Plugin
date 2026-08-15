using System;
using System.Collections.Generic;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// メイドのファイル型プロパティ（＝装備中の menu）のスナップショット。
    /// 操作履歴の undo/redo で「変わった部位だけを元の menu へ戻す」ために使う。
    /// セット衣装やカラーセットのように複数部位が同時に書き換わる操作も、
    /// 差分をまとめて戻すことで一様に扱える
    /// </summary>
    public class MaidPropsSnapshot
    {
        /// <summary>MaidProp.type がファイル型（menu を持つ部位）を表す値</summary>
        private const int FilePropType = 3;

        private static readonly MPN[] AllMPNs = (MPN[])Enum.GetValues(typeof(MPN));

        private struct PropState
        {
            public string fileName;
            public int rid;
        }

        private readonly Maid _maid;
        private readonly Dictionary<MPN, PropState> _props;

        private MaidPropsSnapshot(Maid maid, Dictionary<MPN, PropState> props)
        {
            _maid = maid;
            _props = props;
        }

        /// <summary>現在の装備状態を控える。maid が null なら null を返す</summary>
        public static MaidPropsSnapshot Capture(Maid maid)
        {
            if (maid == null)
            {
                return null;
            }

            var props = new Dictionary<MPN, PropState>(AllMPNs.Length);
            foreach (var mpn in AllMPNs)
            {
                var prop = GetFileProp(maid, mpn);
                if (prop == null)
                {
                    continue;
                }

                props[mpn] = new PropState
                {
                    fileName = prop.strFileName,
                    rid = prop.nFileNameRID,
                };
            }

            return new MaidPropsSnapshot(maid, props);
        }

        private static MaidProp GetFileProp(Maid maid, MPN mpn)
        {
            try
            {
                var prop = maid.GetProp(mpn);
                return prop != null && prop.type == FilePropType ? prop : null;
            }
            catch (Exception)
            {
                // MPN の定義数と Maid 内部配列の長さがずれると添字外になる
                return null;
            }
        }

        /// <summary>対象メイドがまだ生きているか（履歴エントリの canApply 用）</summary>
        public bool isAlive => _maid != null;

        /// <summary>
        /// 他のスナップショットと 1 部位でも差があるか。
        /// 変化が無い操作を履歴に積まないための判定に使う
        /// </summary>
        public bool HasDiffFrom(MaidPropsSnapshot other)
        {
            if (other == null || other._maid != _maid)
            {
                return true;
            }

            foreach (var pair in _props)
            {
                PropState otherState;
                if (!other._props.TryGetValue(pair.Key, out otherState)
                    || !IsSameState(pair.Value, otherState))
                {
                    return true;
                }
            }

            // 相手にしか無い部位（このスナップショット時点では未設定だった部位）も差分
            foreach (var key in other._props.Keys)
            {
                if (!_props.ContainsKey(key))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 現在の状態と差のある部位だけをこのスナップショットの内容へ戻す。
        /// 何度呼んでも同じ結果になるため、履歴のジャンプで連続適用されても壊れない
        /// </summary>
        public void Restore()
        {
            if (_maid == null)
            {
                return;
            }

            var changed = false;
            foreach (var pair in _props)
            {
                var prop = GetFileProp(_maid, pair.Key);
                if (prop == null)
                {
                    continue;
                }

                var state = pair.Value;
                if (IsSameState(new PropState { fileName = prop.strFileName, rid = prop.nFileNameRID }, state))
                {
                    continue;
                }

                // 同名指定では boDut が立たず menu が再処理されないため、差がある部位だけ書き戻す
                if (string.IsNullOrEmpty(state.fileName))
                {
                    // 未設定だった部位は menu 名が無いため、脱衣用 menu で外す
                    _maid.DelProp(pair.Key);
                }
                else
                {
                    _maid.SetProp(pair.Key, state.fileName, state.rid, false, false);
                }
                changed = true;
            }

            if (changed)
            {
                _maid.AllProcPropSeqStart();
            }
        }

        private static bool IsSameState(PropState a, PropState b)
        {
            return a.fileName == b.fileName && a.rid == b.rid;
        }
    }
}
