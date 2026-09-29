using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 表示中の一覧をタグで絞り込む。フォルダは配下のアイテムのタグも選択肢にし、
    /// 絞り込み中は該当するアイテム (またはタグ未確定のアイテム) を配下に含むものだけ残す
    /// </summary>
    public class ItemTagFilter
    {
        /// <summary>絞り込まない (「すべて」) ことを表すタグ</summary>
        public const string AllTags = "";

        private string _selectedTag = AllTags;

        /// <summary>選択中のタグ。フォルダを移動しても維持する</summary>
        public string selectedTag
        {
            get => _selectedTag;
            set => _selectedTag = value ?? AllTags;
        }

        public bool isActive => _selectedTag != AllTags;

        /// <summary>絞り込み結果。Refresh が作り直す</summary>
        public TempDirItem filteredItem { get; } = new TempDirItem
        {
            children = new List<ITileViewContent>(1024),
            // 作り直しのたびにツリーの更新を通知すると、自分の作り直しが止まらなくなる
            notifiesTreeChange = false,
        };

        /// <summary>フォルダ配下のタグ。表示順を保つ一覧と、一致判定用の集合を持つ</summary>
        private class DescendantTags
        {
            public readonly List<string> orderedTags = new List<string>();
            public readonly HashSet<string> tagSet = new HashSet<string>();

            /// <summary>タグ未確定 (読み込み前のプリセット) のアイテムを含むか</summary>
            public bool hasUnknown;
        }

        // 前回作ったときの条件。すべて一致する間は作り直さない。
        // 直下の子は検索結果などで件数が同じまま入れ替わるため参照を並び順ごと比べ、
        // それより深い変化はツリーの更新番号で拾う
        private DirItem _builtSource = null;
        private string _builtTag = null;
        private int _builtTreeVersion = -1;
        private int _builtPresetVersion = -1;
        private readonly List<ITileViewContent> _builtSourceChildren = new List<ITileViewContent>(1024);

        /// <summary>フォルダ -> 配下のタグ。フォルダを行き来するたびに配下を数え直さないよう覚えておく</summary>
        private readonly Dictionary<ITileViewContent, DescendantTags> _descendantTagsCache
            = new Dictionary<ITileViewContent, DescendantTags>();
        private int _cacheTreeVersion = -1;
        private int _cachePresetVersion = -1;

        private readonly HashSet<string> _seenTags = new HashSet<string>();

        /// <summary>フォルダ配下のアイテムを集める作業用。毎回の割り当てを避けるため使い回す</summary>
        private readonly List<ITileViewContent> _workFiles = new List<ITileViewContent>(1024);

        private static MaidPresetManager maidPresetManager => MaidPresetManager.instance;

        /// <summary>次の Refresh で必ず作り直させる</summary>
        public void Invalidate()
        {
            _builtSource = null;
        }

        /// <summary>
        /// 絞り込み結果とタグの選択肢を作り直す。一覧が変わっていなければ何もしない。
        /// アイテムツリーを読むので、ロード中 (ワーカーが書き換えている間) は呼ばないこと
        /// </summary>
        public void Refresh(DirItem source, List<string> tagOptions)
        {
            var treeVersion = ModItemManager.treeVersion;
            var presetVersion = maidPresetManager.loadedVersion;
            if (IsUpToDate(source, treeVersion, presetVersion))
            {
                return;
            }

            _builtSource = source;
            _builtTag = _selectedTag;
            _builtTreeVersion = treeVersion;
            _builtPresetVersion = presetVersion;
            _builtSourceChildren.Clear();
            if (source?.children != null)
            {
                _builtSourceChildren.AddRange(source.children);
            }

            if (_cacheTreeVersion != treeVersion || _cachePresetVersion != presetVersion)
            {
                _descendantTagsCache.Clear();
                _cacheTreeVersion = treeVersion;
                _cachePresetVersion = presetVersion;
            }

            tagOptions.Clear();
            tagOptions.Add(AllTags);
            _seenTags.Clear();

            filteredItem.itemPath = source?.itemPath ?? "";
            filteredItem.RemoveAllChildren();

            if (source?.children != null)
            {
                foreach (var child in source.children)
                {
                    bool matches;
                    if (child.isDir)
                    {
                        var descendantTags = GetDescendantTags(child);
                        foreach (var tag in descendantTags.orderedTags)
                        {
                            AddTagOption(tag, tagOptions);
                        }
                        matches = descendantTags.hasUnknown || descendantTags.tagSet.Contains(_selectedTag);
                    }
                    else
                    {
                        var tag = PeekTag(child);
                        AddTagOption(tag, tagOptions);
                        // 未確定のものは隠さない。読み込みが終われば作り直して正しく絞られる
                        matches = tag == null || tag == _selectedTag;
                    }

                    // フラットビューと同じく、親を書き換えずに children へ直接積む
                    if (isActive && matches)
                    {
                        filteredItem.children.Add(child);
                    }
                }
            }

            // 移動先に無いタグでも、選択中であることが見えるよう選択肢に残す
            if (!tagOptions.Contains(_selectedTag))
            {
                tagOptions.Add(_selectedTag);
            }
        }

        private bool IsUpToDate(DirItem source, int treeVersion, int presetVersion)
        {
            if (source != _builtSource
                || _builtTag != _selectedTag
                || _builtTreeVersion != treeVersion
                || _builtPresetVersion != presetVersion)
            {
                return false;
            }

            var children = source?.children;
            var builtCount = _builtSourceChildren.Count;
            if ((children?.Count ?? 0) != builtCount)
            {
                return false;
            }

            for (var i = 0; i < builtCount; i++)
            {
                if (!ReferenceEquals(children[i], _builtSourceChildren[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private DescendantTags GetDescendantTags(ITileViewContent dir)
        {
            DescendantTags result;
            if (_descendantTagsCache.TryGetValue(dir, out result))
            {
                return result;
            }

            result = new DescendantTags();
            _workFiles.Clear();
            dir.GetAllFiles(_workFiles);

            foreach (var file in _workFiles)
            {
                var tag = PeekTag(file);
                if (tag == null)
                {
                    result.hasUnknown = true;
                }
                else if (tag.Length > 0 && result.tagSet.Add(tag))
                {
                    result.orderedTags.Add(tag);
                }
            }

            // 使い回しのリストにアイテムへの参照を残さない
            _workFiles.Clear();

            _descendantTagsCache[dir] = result;
            return result;
        }

        /// <summary>まだ選択肢に無いタグを足す</summary>
        private void AddTagOption(string tag, List<string> tagOptions)
        {
            if (!string.IsNullOrEmpty(tag) && _seenTags.Add(tag))
            {
                tagOptions.Add(tag);
            }
        }

        /// <summary>
        /// 絞り込み用のタグ。プリセットは読み込み済みのときだけ返し、絞り込みのために
        /// 読み込みやサムネ生成を起こさない (配下の全件を読むと重く、メモリも食う)。未確定なら null
        /// </summary>
        private static string PeekTag(ITileViewContent item)
        {
            var presetItem = item as PresetItem;
            if (presetItem != null)
            {
                return presetItem.loadedTag;
            }
            return item.tag ?? "";
        }
    }
}
