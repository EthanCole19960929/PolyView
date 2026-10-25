using System.IO;
using System.Numerics;
using Silk.NET.Assimp;
using AssimpScene = Silk.NET.Assimp.Scene;

namespace PolyView;

public static unsafe partial class AssimpImporter
{
    public static SkeletalAnimation LoadAnimation(string modelPath, MeshData target, string animationPath, int clipIndex = 0)
    {
        using var api = Assimp.GetApi();
        var animationScene = api.ImportFile(animationPath, (uint)Steps);
        if (animationScene == null) throw new InvalidDataException(api.GetErrorStringS());
        AssimpScene* modelScene = null;
        try
        {
            if (clipIndex < 0 || clipIndex >= animationScene->MNumAnimations) throw new InvalidDataException("动画片段不存在。");
            var animation = animationScene->MAnimations[clipIndex];
            double ticks = animation->MTicksPerSecond;
            double duration = animation->MDuration / ticks;
            if (!(ticks > 0) || !double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("动画没有有效的时长或时间基准。");
            bool sameFile = Path.GetFullPath(modelPath).Equals(Path.GetFullPath(animationPath), StringComparison.OrdinalIgnoreCase);
            modelScene = sameFile ? animationScene : api.ImportFile(modelPath, (uint)Steps);
            if (modelScene == null) throw new InvalidDataException(api.GetErrorStringS());

            bool hasModelSkin = HasSkin(modelScene);
            var geometryScene = modelScene;
            string geometryPath = modelPath;
            if (!hasModelSkin && HasSkin(animationScene))
            {
                geometryScene = animationScene; geometryPath = animationPath;
            }
            var mesh = Convert(api, geometryScene, geometryPath);
            if (geometryScene == animationScene && !hasModelSkin && !sameFile)
            {
                // Only use a rigged animation export when it really is this character, not an unrelated model in the folder.
                if (mesh.Vertices.Length != target.Vertices.Length || !mesh.Indices.AsSpan().SequenceEqual(target.Indices))
                    throw new InvalidDataException("动画附带的蒙皮模型与当前模型不匹配，无法直接播放。");
            }

            var nodes = new List<AnimationNode>();
            var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
            var instances = new List<(nint Mesh, int Owner)>();
            var pending = new Stack<(nint Node, int Parent)>();
            pending.Push(((nint)geometryScene->MRootNode, -1));
            while (pending.TryPop(out var item))
            {
                var node = (Node*)item.Node;
                int slot = nodes.Count;
                nodes.Add(new AnimationNode(node->MName.AsString, item.Parent, Matrix4x4.Transpose(node->MTransformation)));
                lookup.TryAdd(node->MName.AsString, slot);
                for (uint i = 0; i < node->MNumMeshes; i++)
                {
                    var source = geometryScene->MMeshes[node->MMeshes[i]];
                    if (((PrimitiveType)source->MPrimitiveTypes & PrimitiveType.Triangle) != 0 && source->MNumVertices > 0)
                        instances.Add(((nint)source, slot));
                }
                for (int i = (int)node->MNumChildren - 1; i >= 0; i--) pending.Push(((nint)node->MChildren[i], slot));
            }
            var tracks = new List<AnimationTrack>();
            for (uint i = 0; i < animation->MNumChannels; i++)
            {
                var channel = animation->MChannels[i];
                if (!lookup.TryGetValue(channel->MNodeName.AsString, out int slot)) continue;
                var pt = new double[channel->MNumPositionKeys]; var positions = new Vector3[pt.Length];
                var rt = new double[channel->MNumRotationKeys]; var rotations = new Quaternion[rt.Length];
                var st = new double[channel->MNumScalingKeys]; var scales = new Vector3[st.Length];
                for (int j = 0; j < pt.Length; j++) { pt[j] = channel->MPositionKeys[j].MTime / ticks; positions[j] = channel->MPositionKeys[j].MValue; }
                for (int j = 0; j < rt.Length; j++) { rt[j] = channel->MRotationKeys[j].MTime / ticks; rotations[j] = channel->MRotationKeys[j].MValue; }
                for (int j = 0; j < st.Length; j++) { st[j] = channel->MScalingKeys[j].MTime / ticks; scales[j] = channel->MScalingKeys[j].MValue; }
                tracks.Add(new AnimationTrack(slot, pt, positions, rt, rotations, st, scales));
            }
            if (tracks.Count == 0) throw new InvalidDataException("当前模型没有匹配的骨骼和蒙皮权重。请先绑定骨骼，或使用带蒙皮模型的动画 FBX。");
            var localPositions = new Vector3[mesh.Vertices.Length]; var localNormals = new Vector3[mesh.Vertices.Length];
            var owners = new int[mesh.Vertices.Length]; var bones = new List<SkinBone>();
            int offset = 0;
            foreach (var instance in instances)
            {
                var source = (Silk.NET.Assimp.Mesh*)instance.Mesh;
                for (int i = 0; i < source->MNumVertices; i++)
                {
                    localPositions[offset + i] = source->MVertices[i];
                    localNormals[offset + i] = source->MNormals == null ? Vector3.UnitY : source->MNormals[i];
                    owners[offset + i] = instance.Owner;
                }
                for (uint b = 0; b < source->MNumBones; b++)
                {
                    var bone = source->MBones[b];
                    if (!lookup.TryGetValue(bone->MName.AsString, out int slot)) throw new InvalidDataException($"缺少骨骼节点：{bone->MName.AsString}");
                    var weights = new (int Vertex, float Weight)[bone->MNumWeights];
                    for (int w = 0; w < weights.Length; w++)
                    {
                        var weight = bone->MWeights[w];
                        if (weight.MVertexId >= source->MNumVertices || !float.IsFinite(weight.MWeight) || weight.MWeight < 0)
                            throw new InvalidDataException("骨骼权重无效。");
                        weights[w] = (offset + (int)weight.MVertexId, weight.MWeight);
                    }
                    bones.Add(new SkinBone(slot, Matrix4x4.Transpose(bone->MOffsetMatrix), weights));
                }
                offset += (int)source->MNumVertices;
            }
            if (offset != mesh.Vertices.Length) throw new InvalidDataException("动画顶点布局与导入网格不一致。");
            // A channel must affect a mesh or one of its skin joints; matching an unrelated root name is insufficient.
            var affected = new HashSet<int>();
            foreach (int node in owners.Concat(bones.Select(b => b.Node)).Distinct())
                for (int p = node; p >= 0; p = nodes[p].Parent) affected.Add(p);
            if (!tracks.Any(t => affected.Contains(t.Node))) throw new InvalidDataException("动画没有可驱动当前模型的骨骼或节点。");

            float sourceHeight = mesh.Max.Y - mesh.Min.Y;
            float scale = sourceHeight > 1e-8f ? (target.Max.Y - target.Min.Y) / sourceHeight : 1;
            var translation = (target.Min + target.Max) * 0.5f - (mesh.Min + mesh.Max) * (0.5f * scale);
            if (geometryScene == animationScene && !hasModelSkin && !sameFile)
            {
                float tolerance = MathF.Max((target.Max - target.Min).Length() * 0.01f, 1e-5f);
                for (int i = 0; i < mesh.Vertices.Length; i++)
                    if (Vector3.Distance(mesh.Vertices[i].Position * scale + translation, target.Vertices[i].Position) > tolerance)
                        throw new InvalidDataException("动画附带模型的形状与当前模型不一致，不能直接套用骨骼。");
            }
            mesh.Nodes = target.Nodes; mesh.World = target.World;
            return new SkeletalAnimation(mesh, nodes.ToArray(), tracks.ToArray(), bones.ToArray(), localPositions, localNormals, owners, duration, scale, translation);
        }
        finally
        {
            if (modelScene != null && modelScene != animationScene) api.ReleaseImport(modelScene);
            api.ReleaseImport(animationScene);
        }
    }

    static bool HasSkin(AssimpScene* scene)
    {
        for (uint i = 0; i < scene->MNumMeshes; i++) if (scene->MMeshes[i]->MNumBones > 0) return true;
        return false;
    }
}
