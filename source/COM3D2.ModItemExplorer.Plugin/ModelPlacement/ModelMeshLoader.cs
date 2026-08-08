using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// ImportCM.LoadSkinMesh_R (COM3D2.5) を bodyskin/morph 非依存に移植したモデル単体ローダー。
    /// メイド装着を前提としない配置用途のため、OriVert 登録・listDEL 管理・スロット別影制御を除いている。
    /// バイナリの読み取り順は移植元と完全に同一に保つこと（1バイトでもずれると以降が全て壊れる）。
    /// 保守は移植元と行単位で照合して行うため、読み取り本体のメソッド分割は照合を壊すので行わない。
    /// </summary>
    public static class ModelMeshLoader
    {
        /// <summary>ゲーム内の空 GameObject テンプレート。移植元と同じくこれを複製してボーンを作る</summary>
        private const string SeedResourceName = "seed";

        /// <summary>
        /// ウェイト参照の有無を追跡するボーン情報。ImportCM.BoneUse (private) の移植。
        /// 移植元の name/idx は本ローダーでは参照しないため持たせていない
        /// </summary>
        private class BoneUse
        {
            public bool use;
            public Transform bone;
            public bool delete;

            public BoneUse(Transform f_bone)
            {
                bone = f_bone;
            }
        }

        /// <summary>
        /// .model ファイルを読み込み、SkinnedMeshRenderer 構築済みのルート GameObject を返す。
        /// 失敗時は警告ログを出して null を返す（プラグイン側でゲームを落とさないため）。
        /// </summary>
        /// <param name="disposables">
        /// 生成した Mesh / Material を積む。移植元は bodyskin.listDEL に積んで TBodySkin 破棄時に
        /// 一括 Destroy するが、bodyskin に null を渡す本ローダーでは破棄責任が呼び出し側に移る。
        /// なお ReadMaterial が内部で生成する Texture2D は、共有アセット由来のテクスチャと
        /// 区別できないため追跡していない（誤って共有テクスチャを壊すより解放漏れを選ぶ）
        /// </param>
        public static GameObject LoadMesh(string modelFileName, int layer, List<UnityEngine.Object> disposables)
        {
            try
            {
                byte[] buffer;
                using (var fileBase = GameUty.FileOpen(modelFileName))
                {
                    if (fileBase == null || !fileBase.IsValid())
                    {
                        MTEUtils.LogWarning("モデルファイルが開けませんでした。{0}", modelFileName);
                        return null;
                    }
                    buffer = fileBase.ReadAll();
                }

                return LoadMeshInternal(buffer, modelFileName, layer, disposables);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("モデルの読み込みに失敗しました。{0}", modelFileName);
                MTEUtils.LogException(e);
                return null;
            }
        }

        private static GameObject LoadMeshInternal(
            byte[] buffer,
            string modelFileName,
            int layer,
            List<UnityEngine.Object> disposables)
        {
            var rootGo = UnityEngine.Object.Instantiate(Resources.Load(SeedResourceName)) as GameObject;
            try
            {
                return ReadModel(buffer, modelFileName, layer, disposables, rootGo);
            }
            catch
            {
                // 途中で失敗するとボーン群ごとシーンに取り残されるため、ここで確実に片付ける
                UnityEngine.Object.Destroy(rootGo);
                throw;
            }
        }

        private static GameObject ReadModel(
            byte[] buffer,
            string modelFileName,
            int layer,
            List<UnityEngine.Object> disposables,
            GameObject rootGo)
        {
            var isCrcModel = modelFileName.Contains("crc_")
                || modelFileName.Contains("crx_")
                || modelFileName.Contains("gp03_");

            var binaryReader = new BinaryReader(new MemoryStream(buffer), Encoding.UTF8);

            rootGo.layer = layer;

            GameObject meshRootGo = null;
            var boneTable = new Hashtable();

            var header = binaryReader.ReadString();
            if (header != "CM3D2_MESH")
            {
                MTEUtils.LogWarning("ヘッダーが不正です。{0} header={1}", modelFileName, header);
                UnityEngine.Object.Destroy(rootGo);
                return null;
            }

            var version = binaryReader.ReadInt32();
            var modelName = binaryReader.ReadString();
            rootGo.name = "_SM_" + modelName;

            var meshRootBoneName = binaryReader.ReadString();
            var boneUseDic = new Dictionary<string, BoneUse>();
            var boneCount = binaryReader.ReadInt32();
            var boneGoList = new List<GameObject>();

            string shadowCastingModeName = null;
            if (2104 <= version && version < 2200)
            {
                shadowCastingModeName = binaryReader.ReadString();
            }

            for (int i = 0; i < boneCount; i++)
            {
                var boneGo = UnityEngine.Object.Instantiate(Resources.Load(SeedResourceName)) as GameObject;
                boneGo.layer = layer;
                boneGo.name = binaryReader.ReadString();
                boneGoList.Add(boneGo);
                if (boneGo.name == meshRootBoneName)
                {
                    meshRootGo = boneGo;
                }
                boneTable[boneGo.name] = boneGo;
                boneUseDic[boneGo.name] = new BoneUse(boneGo.transform);

                if (binaryReader.ReadByte() != 0)
                {
                    var sclGo = UnityEngine.Object.Instantiate(Resources.Load(SeedResourceName)) as GameObject;
                    sclGo.name = boneGo.name + "_SCL_";
                    sclGo.transform.parent = boneGo.transform;
                    boneTable[boneGo.name + "&_SCL_"] = sclGo;
                    boneUseDic[sclGo.name] = new BoneUse(sclGo.transform);
                }
            }

            for (int i = 0; i < boneCount; i++)
            {
                var parentIndex = binaryReader.ReadInt32();
                if (parentIndex >= 0)
                {
                    boneGoList[i].transform.parent = boneGoList[parentIndex].transform;
                }
                else
                {
                    boneGoList[i].transform.parent = rootGo.transform;
                }
            }

            for (int i = 0; i < boneCount; i++)
            {
                var transform = boneGoList[i].transform;
                var px = binaryReader.ReadSingle();
                var py = binaryReader.ReadSingle();
                var pz = binaryReader.ReadSingle();
                transform.localPosition = new Vector3(px, py, pz);

                var rx = binaryReader.ReadSingle();
                var ry = binaryReader.ReadSingle();
                var rz = binaryReader.ReadSingle();
                var rw = binaryReader.ReadSingle();
                transform.localRotation = new Quaternion(rx, ry, rz, rw);

                if (2001 <= version && binaryReader.ReadBoolean())
                {
                    var sx = binaryReader.ReadSingle();
                    var sy = binaryReader.ReadSingle();
                    var sz = binaryReader.ReadSingle();
                    transform.localScale = new Vector3(sx, sy, sz);
                }
            }

            var vertexCount = binaryReader.ReadInt32();
            var subMeshCount = binaryReader.ReadInt32();
            var boneRefCount = binaryReader.ReadInt32();

            if (meshRootGo == null)
            {
                MTEUtils.LogWarning("メッシュのルートボーンが見つかりませんでした。{0} bone={1}", modelFileName, meshRootBoneName);
                UnityEngine.Object.Destroy(rootGo);
                return null;
            }

            var smr = meshRootGo.AddComponent<SkinnedMeshRenderer>();
            smr.updateWhenOffscreen = true;
            smr.skinnedMotionVectors = false;
            smr.lightProbeUsage = LightProbeUsage.Off;
            smr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            smr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            if (!string.IsNullOrEmpty(shadowCastingModeName))
            {
                smr.shadowCastingMode = (ShadowCastingMode)Enum.Parse(typeof(ShadowCastingMode), shadowCastingModeName);
            }

            var boneUses = new BoneUse[boneRefCount];
            var bones = new Transform[boneRefCount];
            for (int i = 0; i < boneRefCount; i++)
            {
                var boneName = binaryReader.ReadString();
                if (!boneTable.ContainsKey(boneName))
                {
                    MTEUtils.LogWarning("参照ボーンが見つかりませんでした。nullbone={0}", boneName);
                    continue;
                }

                var boneGo = boneTable.ContainsKey(boneName + "&_SCL_")
                    ? (GameObject)boneTable[boneName + "&_SCL_"]
                    : (GameObject)boneTable[boneName];
                bones[i] = boneGo.transform;
                boneUses[i] = new BoneUse(boneGo.transform);
                boneUseDic[boneGo.name] = boneUses[i];
            }
            smr.bones = bones;

            var mesh = new Mesh();
            smr.sharedMesh = mesh;
            disposables.Add(mesh);

            var bindposes = new Matrix4x4[boneRefCount];
            for (int i = 0; i < boneRefCount; i++)
            {
                for (int j = 0; j < 16; j++)
                {
                    bindposes[i][j] = binaryReader.ReadSingle();
                }
            }
            mesh.bindposes = bindposes;

            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];
            Vector2[] uv2 = null;
            Vector2[] uv3 = null;
            Vector2[] uv4 = null;
            var boneWeights = new BoneWeight[vertexCount];

            var hasUv2 = false;
            var hasUv3 = false;
            var hasUv4 = false;
            var hasUv5 = false;
            var hasUv6 = false;
            var hasUv7 = false;
            var hasUv8 = false;
            if (2101 <= version)
            {
                if (hasUv2 = binaryReader.ReadBoolean())
                {
                    uv2 = new Vector2[vertexCount];
                }
                if (hasUv3 = binaryReader.ReadBoolean())
                {
                    uv3 = new Vector2[vertexCount];
                }
                if (hasUv4 = binaryReader.ReadBoolean())
                {
                    uv4 = new Vector2[vertexCount];
                }
                hasUv5 = binaryReader.ReadBoolean();
                hasUv6 = binaryReader.ReadBoolean();
                hasUv7 = binaryReader.ReadBoolean();
                hasUv8 = binaryReader.ReadBoolean();
            }

            for (int i = 0; i < vertexCount; i++)
            {
                var x = binaryReader.ReadSingle();
                var y = binaryReader.ReadSingle();
                var z = binaryReader.ReadSingle();
                vertices[i].Set(x, y, z);

                x = binaryReader.ReadSingle();
                y = binaryReader.ReadSingle();
                z = binaryReader.ReadSingle();
                normals[i].Set(x, y, z);

                x = binaryReader.ReadSingle();
                y = binaryReader.ReadSingle();
                uv[i].Set(x, y);

                if (hasUv2)
                {
                    x = binaryReader.ReadSingle();
                    y = binaryReader.ReadSingle();
                    uv2[i].Set(x, y);
                }
                if (hasUv3)
                {
                    x = binaryReader.ReadSingle();
                    y = binaryReader.ReadSingle();
                    uv3[i].Set(x, y);
                }
                if (hasUv4)
                {
                    x = binaryReader.ReadSingle();
                    y = binaryReader.ReadSingle();
                    uv4[i].Set(x, y);
                }
                // uv5〜uv8 は Unity 5.6 の Mesh が持たないため読み飛ばすだけ（移植元も同様）
                if (hasUv5)
                {
                    binaryReader.ReadSingle();
                    binaryReader.ReadSingle();
                }
                if (hasUv6)
                {
                    binaryReader.ReadSingle();
                    binaryReader.ReadSingle();
                }
                if (hasUv7)
                {
                    binaryReader.ReadSingle();
                    binaryReader.ReadSingle();
                }
                if (hasUv8)
                {
                    binaryReader.ReadSingle();
                    binaryReader.ReadSingle();
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.uv2 = uv2;
            mesh.uv3 = uv3;
            mesh.uv4 = uv4;

            var tangentCount = binaryReader.ReadInt32();
            if (tangentCount > 0)
            {
                var tangents = new Vector4[tangentCount];
                for (int i = 0; i < tangentCount; i++)
                {
                    var x = binaryReader.ReadSingle();
                    var y = binaryReader.ReadSingle();
                    var z = binaryReader.ReadSingle();
                    var w = binaryReader.ReadSingle();
                    tangents[i] = new Vector4(x, y, z, w);
                }
                mesh.tangents = tangents;
            }
            else
            {
                mesh.RecalculateTangents();
                var tangentList = new List<Vector4>();
                mesh.GetTangents(tangentList);
                for (int i = 0; i < tangentList.Count; i++)
                {
                    var tangent = tangentList[i];
                    if (!Mathf.Approximately(tangent.w, -1f))
                    {
                        tangent.x *= -1f;
                        tangent.w *= -1f;
                        tangentList[i] = tangent;
                    }
                }
                mesh.SetTangents(tangentList);
            }

            for (int i = 0; i < vertexCount; i++)
            {
                var index0 = boneWeights[i].boneIndex0 = binaryReader.ReadUInt16();
                var index1 = boneWeights[i].boneIndex1 = binaryReader.ReadUInt16();
                var index2 = boneWeights[i].boneIndex2 = binaryReader.ReadUInt16();
                var index3 = boneWeights[i].boneIndex3 = binaryReader.ReadUInt16();

                MarkBoneUsed(boneUses, index0);
                MarkBoneUsed(boneUses, index1);
                MarkBoneUsed(boneUses, index2);
                MarkBoneUsed(boneUses, index3);

                boneWeights[i].weight0 = binaryReader.ReadSingle();
                boneWeights[i].weight1 = binaryReader.ReadSingle();
                boneWeights[i].weight2 = binaryReader.ReadSingle();
                boneWeights[i].weight3 = binaryReader.ReadSingle();
            }

            BoneUse bip01;
            if (!isCrcModel && boneUseDic.TryGetValue("Bip01", out bip01))
            {
                RemoveNoWeightBone(boneUseDic, bip01.bone);
            }

            mesh.boneWeights = boneWeights;
            mesh.subMeshCount = subMeshCount;

            for (int i = 0; i < subMeshCount; i++)
            {
                var triangleCount = binaryReader.ReadInt32();
                var triangles = new int[triangleCount];
                for (int j = 0; j < triangleCount; j++)
                {
                    triangles[j] = binaryReader.ReadUInt16();
                }
                mesh.SetTriangles(triangles, i);
            }

            var materialCount = binaryReader.ReadInt32();
            var materials = new Material[materialCount];
            for (int i = 0; i < materialCount; i++)
            {
#if COM3D25
                // 2.5 は親モデルのバージョンでマテリアルの読み方を変えるため渡す
                materials[i] = ImportCM.ReadMaterial(binaryReader, null, null, version);
#else
                materials[i] = ImportCM.ReadMaterial(binaryReader, null, null);
#endif
                disposables.Add(materials[i]);
            }
            // sharedMaterials に入れる。materials の setter/getter は複製を作るため、
            // ここで生成したインスタンスと disposables の追跡対象がずれてしまう
            smr.sharedMaterials = materials;

            // 移植元はこの後 morph ブロックと skinThick を読むが、配置用途では両方不要。
            // morph のペイロード長は TMorph 側でしか解釈できずスキップできないため、ここで読み取りを打ち切る
            binaryReader.Close();
            return rootGo;
        }

        private static void MarkBoneUsed(BoneUse[] boneUses, int index)
        {
            if (index >= 0 && index < boneUses.Length && boneUses[index] != null)
            {
                boneUses[index].use = true;
            }
        }

        /// <summary>
        /// ウェイト参照されていないボーンを枝ごと削除する。ImportCM.RemoveNoWeightBone (private) の移植。
        /// </summary>
        private static BoneUse RemoveNoWeightBone(Dictionary<string, BoneUse> dic, Transform parent)
        {
            var used = false;
            if (parent.name == "Skirt")
            {
                return null;
            }

            var children = new BoneUse[parent.childCount];
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = RemoveNoWeightBone(dic, parent.GetChild(i));
                children[i] = child;
                // 子が null = 判定対象外（Skirt 配下や辞書に無いボーン）。消せないので使用中扱いにする
                used |= child?.use ?? true;
            }

            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child != null && child.bone != null && child.delete)
                {
                    UnityEngine.Object.DestroyImmediate(child.bone.gameObject);
                    child.bone = null;
                }
            }

            if (used)
            {
                return null;
            }

            BoneUse self;
            if (dic.TryGetValue(parent.name, out self))
            {
                // ここに来る時点で used は必ず false（true なら上で return 済み）
                self.delete = !self.use;
                return self;
            }
            return null;
        }
    }
}
