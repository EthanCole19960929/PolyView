using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace PolyView;

/// <summary>
/// 解开 GLB 里的 KHR_draco_mesh_compression：用 Google Draco 的解码库（draco_decoder.dll，Apache-2.0，
/// 由 google/draco 1.5.7 的 DRACO_UNITY_PLUGIN 目标编译）把压缩的几何体解成普通 accessor，
/// 写回一份新的 GLB 交给 SharpGLTF。没有 Draco 的文件不经过这里。
/// </summary>
public static class DracoGlb
{
    const string Extension = "KHR_draco_mesh_compression";

    /// <summary>GLB 是否用了 Draco 压缩（只读 JSON 头，不解码）。</summary>
    public static bool UsesDraco(byte[] glb)
    {
        return TryReadGlb(glb, out var json, out _) && json.Contains(Extension, StringComparison.Ordinal);
    }

    public static bool DecoderAvailable
    {
        get
        {
            try
            {
                return NativeLibrary.TryLoad(Native.LibraryName, typeof(DracoGlb).Assembly, null, out _);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>返回解压后的 GLB 字节。</summary>
    public static byte[] Decompress(byte[] glb)
    {
        if (!TryReadGlb(glb, out var jsonText, out var bin))
        {
            throw new InvalidDataException("不是有效的 GLB 文件。");
        }

        if (!DecoderAvailable)
        {
            throw new DracoNotSupportedException("该 GLB 使用 Draco 压缩，但找不到 Draco 解码库 draco_decoder.dll，暂不支持。");
        }

        var root = JsonNode.Parse(jsonText)!.AsObject();
        var bufferViews = root["bufferViews"]!.AsArray();
        var accessors = root["accessors"]!.AsArray();
        var output = new MemoryStream();
        output.Write(bin);
        var decodedViews = new Dictionary<int, (List<int> Faces, int Vertices)>();

        foreach (var mesh in root["meshes"]?.AsArray() ?? [])
        {
            foreach (var primitiveNode in mesh!["primitives"]!.AsArray())
            {
                var primitive = primitiveNode!.AsObject();
                if (primitive["extensions"]?[Extension] is not JsonObject draco)
                {
                    continue;
                }

                int viewIndex = (int)draco["bufferView"]!;
                var view = bufferViews[viewIndex]!;
                if (view["buffer"] is JsonNode buffer && (int)buffer != 0)
                {
                    throw new InvalidDataException("Draco 数据不在 GLB 内置缓冲里，暂不支持。");
                }

                int offset = (int?)view["byteOffset"] ?? 0;
                int length = (int)view["byteLength"]!;
                DecodePrimitive(bin.AsSpan(offset, length), primitive, draco, accessors, bufferViews, output);
                var extensions = primitive["extensions"]!.AsObject();
                extensions.Remove(Extension);
                if (extensions.Count == 0)
                {
                    primitive.Remove("extensions");
                }
            }
        }

        RemoveName(root["extensionsUsed"] as JsonArray, Extension);
        RemoveName(root["extensionsRequired"] as JsonArray, Extension);
        Pad(output, 0);
        root["buffers"]![0]!["byteLength"] = output.Length;
        return WriteGlb(root.ToJsonString(), output.ToArray());
    }

    static unsafe void DecodePrimitive(ReadOnlySpan<byte> compressed, JsonObject primitive, JsonObject draco, JsonArray accessors, JsonArray bufferViews, MemoryStream output)
    {
        Native.DracoMesh* mesh = null;
        fixed (byte* data = compressed)
        {
            if (Native.DecodeDracoMesh(data, (uint)compressed.Length, &mesh) < 0 || mesh == null)
            {
                throw new InvalidDataException("Draco 解码失败，文件可能已损坏。");
            }
        }

        try
        {
            int vertexCount = mesh->num_vertices;
            int faceCount = mesh->num_faces;

            if (primitive["indices"] is JsonNode indicesNode)
            {
                Native.DracoData* indices = null;
                if (!Native.GetMeshIndices(mesh, &indices) || indices == null)
                {
                    throw new InvalidDataException("Draco 读取索引失败。");
                }

                try
                {
                    var bytes = new ReadOnlySpan<byte>(indices->data, faceCount * 3 * 4);
                    int view = AppendView(output, bufferViews, bytes, target: 34963);
                    RewriteAccessor(accessors[(int)indicesNode]!.AsObject(), view, 5125, faceCount * 3, normalized: false);
                }
                finally
                {
                    Native.ReleaseDracoData(&indices);
                }
            }

            var attributes = primitive["attributes"]!.AsObject();
            foreach (var (name, idNode) in draco["attributes"]!.AsObject())
            {
                if (idNode == null || attributes[name] is not JsonNode accessorIndex)
                {
                    continue;
                }

                Native.DracoAttribute* attribute = null;
                if (!Native.GetAttributeByUniqueId(mesh, (int)idNode, &attribute) || attribute == null)
                {
                    throw new InvalidDataException($"Draco 里找不到属性 {name}。");
                }

                Native.DracoData* values = null;
                try
                {
                    if (!Native.GetAttributeData(mesh, attribute, &values) || values == null)
                    {
                        throw new InvalidDataException($"Draco 读取属性 {name} 失败。");
                    }

                    var (componentType, size) = values->data_type switch
                    {
                        Native.DT_INT8 => (5120, 1),
                        Native.DT_UINT8 => (5121, 1),
                        Native.DT_INT16 => (5122, 2),
                        Native.DT_UINT16 => (5123, 2),
                        Native.DT_UINT32 => (5125, 4),
                        Native.DT_FLOAT32 => (5126, 4),
                        _ => throw new InvalidDataException($"Draco 属性 {name} 的数据类型不支持（{values->data_type}）。"),
                    };
                    var accessor = accessors[(int)accessorIndex]!.AsObject();
                    var bytes = new ReadOnlySpan<byte>(values->data, vertexCount * attribute->num_components * size);
                    int view = AppendView(output, bufferViews, bytes, target: 34962);
                    bool normalized = componentType != 5126 && ((bool?)accessor["normalized"] ?? false);
                    RewriteAccessor(accessor, view, componentType, vertexCount, normalized);
                    if (componentType == 5126 && accessor["min"] != null && accessor["max"] != null)
                    {
                        // Draco 是有损量化，解出来的值可能略微超出文件里写的 min / max，按实际数据重算，否则 SharpGLTF 校验不过。
                        UpdateBounds(accessor, new ReadOnlySpan<float>(values->data, vertexCount * attribute->num_components), attribute->num_components);
                    }
                }
                finally
                {
                    if (values != null)
                    {
                        Native.ReleaseDracoData(&values);
                    }

                    Native.ReleaseDracoAttribute(&attribute);
                }
            }
        }
        finally
        {
            Native.ReleaseDracoMesh(&mesh);
        }
    }

    static void UpdateBounds(JsonObject accessor, ReadOnlySpan<float> values, int components)
    {
        var min = new JsonArray();
        var max = new JsonArray();
        for (int c = 0; c < components; c++)
        {
            float lo = float.MaxValue;
            float hi = float.MinValue;
            for (int i = c; i < values.Length; i += components)
            {
                lo = MathF.Min(lo, values[i]);
                hi = MathF.Max(hi, values[i]);
            }

            min.Add(lo);
            max.Add(hi);
        }

        accessor["min"] = min;
        accessor["max"] = max;
    }

    static int AppendView(MemoryStream output, JsonArray bufferViews, ReadOnlySpan<byte> bytes, int target)
    {
        Pad(output, 0);
        long offset = output.Length;
        output.Write(bytes);
        bufferViews.Add(new JsonObject
        {
            ["buffer"] = 0,
            ["byteOffset"] = offset,
            ["byteLength"] = bytes.Length,
            ["target"] = target,
        });
        return bufferViews.Count - 1;
    }

    static void RewriteAccessor(JsonObject accessor, int view, int componentType, int count, bool normalized)
    {
        accessor["bufferView"] = view;
        accessor["byteOffset"] = 0;
        accessor["componentType"] = componentType;
        accessor["count"] = count;
        accessor.Remove("sparse");
        if (normalized)
        {
            accessor["normalized"] = true;
        }
        else
        {
            accessor.Remove("normalized");
        }
    }

    static void RemoveName(JsonArray? array, string name)
    {
        if (array == null)
        {
            return;
        }

        for (int i = array.Count - 1; i >= 0; i--)
        {
            if ((string?)array[i] == name)
            {
                array.RemoveAt(i);
            }
        }
    }

    static void Pad(Stream stream, byte value)
    {
        while (stream.Length % 4 != 0)
        {
            stream.WriteByte(value);
        }
    }

    static bool TryReadGlb(byte[] glb, out string json, out byte[] bin)
    {
        json = "";
        bin = [];
        if (glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(glb) != 0x46546C67)
        {
            return false;
        }

        int position = 12;
        while (position + 8 <= glb.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(position));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(position + 4));
            if (length < 0 || position + 8 + length > glb.Length)
            {
                return false;
            }

            if (type == 0x4E4F534A)
            {
                json = Encoding.UTF8.GetString(glb, position + 8, length);
            }
            else if (type == 0x004E4942)
            {
                bin = glb.AsSpan(position + 8, length).ToArray();
            }

            position += 8 + length;
        }

        return json.Length > 0;
    }

    static byte[] WriteGlb(string json, byte[] bin)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        int jsonLength = (jsonBytes.Length + 3) & ~3;
        int total = 12 + 8 + jsonLength + 8 + bin.Length;
        var result = new byte[total];
        var span = result.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)total);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 0x4E4F534A);
        jsonBytes.CopyTo(span[20..]);
        span.Slice(20 + jsonBytes.Length, jsonLength - jsonBytes.Length).Fill((byte)' ');
        int binStart = 20 + jsonLength;
        BinaryPrimitives.WriteUInt32LittleEndian(span[binStart..], (uint)bin.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(binStart + 4)..], 0x004E4942);
        bin.CopyTo(span[(binStart + 8)..]);
        return result;
    }

    /// <summary>draco_unity_plugin.h 的 C 接口（google/draco，src/draco/unity）。</summary>
    static unsafe class Native
    {
        public const string LibraryName = "draco_decoder";

        public const int DT_INT8 = 1;
        public const int DT_UINT8 = 2;
        public const int DT_INT16 = 3;
        public const int DT_UINT16 = 4;
        public const int DT_INT32 = 5;
        public const int DT_UINT32 = 6;
        public const int DT_FLOAT32 = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct DracoMesh
        {
            public int num_faces;
            public int num_vertices;
            public int num_attributes;
            public void* private_mesh;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DracoAttribute
        {
            public int attribute_type;
            public int data_type;
            public int num_components;
            public int unique_id;
            public void* private_attribute;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DracoData
        {
            public int data_type;
            public void* data;
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int DecodeDracoMesh(byte* data, uint length, DracoMesh** mesh);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool GetMeshIndices(DracoMesh* mesh, DracoData** indices);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool GetAttributeByUniqueId(DracoMesh* mesh, int uniqueId, DracoAttribute** attribute);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool GetAttributeData(DracoMesh* mesh, DracoAttribute* attribute, DracoData** data);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ReleaseDracoMesh(DracoMesh** mesh);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ReleaseDracoAttribute(DracoAttribute** attribute);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ReleaseDracoData(DracoData** data);
    }
}
