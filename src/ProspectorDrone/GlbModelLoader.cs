// Prospector Drone — edited and maintained by Darkethereal.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace StellarProspectorDrone;

internal static class GlbModelLoader
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinChunk = 0x004E4942;
    private static GameObject _prefab;

    internal static bool TryPrewarm(out string error)
    {
        error = null;
        try
        {
            if (_prefab == null) _prefab = BuildPrefab();
            return _prefab != null;
        }
        catch (Exception exception)
        {
            error = exception.ToString();
            return false;
        }
    }

    internal static GameObject Instantiate(Transform parent)
    {
        if (_prefab == null) _prefab = BuildPrefab();
        if (_prefab == null) return null;
        var instance = UnityEngine.Object.Instantiate(_prefab, parent, false);
        instance.name = "Mining Drone Mk VII";
        instance.SetActive(true);
        return instance;
    }

    private static GameObject BuildPrefab()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("mining_drone_mk7.glb");
        if (stream == null) throw new InvalidOperationException("Embedded mining_drone_mk7.glb was not found.");
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != GlbMagic) throw new InvalidDataException("Drone asset is not a GLB file.");
        if (reader.ReadUInt32() != 2) throw new InvalidDataException("Only glTF 2.0 is supported.");
        reader.ReadUInt32();

        JObject json = null;
        byte[] binary = null;
        while (stream.Position < stream.Length)
        {
            var length = reader.ReadUInt32();
            var type = reader.ReadUInt32();
            var data = reader.ReadBytes((int)length);
            if (type == JsonChunk) json = JObject.Parse(System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0', ' ', '\t', '\r', '\n'));
            else if (type == BinChunk) binary = data;
        }
        if (json == null || binary == null) throw new InvalidDataException("GLB is missing JSON or binary data.");

        var gltfMesh = (JObject)json["meshes"]?[0];
        var primitives = (JArray)gltfMesh?["primitives"];
        if (primitives == null || primitives.Count == 0) throw new InvalidDataException("GLB contains no mesh primitives.");

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int[]>();
        var materialIndices = new List<int>();

        foreach (JObject primitive in primitives)
        {
            var attributes = (JObject)primitive["attributes"];
            var positions = ReadVector3(json, binary, (int)attributes["POSITION"], true);
            var primitiveNormals = attributes["NORMAL"] == null
                ? Enumerable.Repeat(Vector3.up, positions.Length).ToArray()
                : ReadVector3(json, binary, (int)attributes["NORMAL"], true);
            var primitiveUvs = attributes["TEXCOORD_0"] == null
                ? new Vector2[positions.Length]
                : ReadVector2(json, binary, (int)attributes["TEXCOORD_0"]);
            var indices = ReadIndices(json, binary, (int)primitive["indices"]);
            var offset = vertices.Count;
            vertices.AddRange(positions);
            normals.AddRange(primitiveNormals);
            uvs.AddRange(primitiveUvs);
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var swap = indices[i + 1];
                indices[i + 1] = indices[i + 2];
                indices[i + 2] = swap;
            }
            triangles.Add(indices.Select(index => index + offset).ToArray());
            materialIndices.Add(primitive["material"] == null ? -1 : (int)primitive["material"]);
        }

        var mesh = new Mesh
        {
            name = "Mining Drone Mk VII",
            indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = triangles.Count;
        for (var i = 0; i < triangles.Count; i++) mesh.SetTriangles(triangles[i], i, false);
        mesh.RecalculateBounds();
        // The hover highlight only adjusts renderer materials and never reads
        // geometry, so the imported mesh can be released from CPU memory safely.
        mesh.UploadMeshData(true);

        var materials = materialIndices.Select(index => BuildMaterial(json, binary, index)).ToArray();
        var root = new GameObject("Mining Drone Mk VII Prefab");
        root.hideFlags = HideFlags.HideAndDontSave;
        root.SetActive(false);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterials = materials;

        var largest = Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z));
        var scale = largest > 0.001f ? 1.75f / largest : 1f;
        root.transform.localScale = Vector3.one * scale;
        root.transform.localPosition = -Vector3.Scale(mesh.bounds.center, root.transform.localScale);
        root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        return root;
    }

    private static Material BuildMaterial(JObject json, byte[] binary, int materialIndex)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Unlit");
        var material = new Material(shader) { name = "Mining Drone Mk VII Material " + materialIndex };
        if (materialIndex < 0) return material;
        var definition = (JObject)json["materials"]?[materialIndex];
        var pbr = (JObject)definition?["pbrMetallicRoughness"];
        if (pbr?["baseColorFactor"] is JArray color && color.Count >= 4)
        {
            var tint = new Color((float)color[0], (float)color[1], (float)color[2], (float)color[3]);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Color")) material.color = tint;
        }
        if (pbr?["baseColorTexture"]?["index"] != null)
        {
            var texture = ReadTexture(json, binary, (int)pbr["baseColorTexture"]["index"], false);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.mainTexture = texture;
        }
        var metallic = pbr?["metallicFactor"] == null ? 1f : (float)pbr["metallicFactor"];
        var roughness = pbr?["roughnessFactor"] == null ? 1f : (float)pbr["roughnessFactor"];
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1f - roughness);
        if (definition?["normalTexture"]?["index"] != null)
        {
            var normal = ReadTexture(json, binary, (int)definition["normalTexture"]["index"], true);
            if (material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
        }
        return material;
    }

    private static Texture2D ReadTexture(JObject json, byte[] binary, int textureIndex, bool linear)
    {
        var source = (int)json["textures"]?[textureIndex]?["source"];
        var image = (JObject)json["images"]?[source];
        var view = (JObject)json["bufferViews"]?[(int)image["bufferView"]];
        var offset = view["byteOffset"] == null ? 0 : (int)view["byteOffset"];
        var length = (int)view["byteLength"];
        var bytes = new byte[length];
        Buffer.BlockCopy(binary, offset, bytes, 0, length);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)
        {
            name = image["name"]?.ToString() ?? "Mining Drone Texture",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear
        };
        var imageConversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
        var loadImage = imageConversion?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
        if (loadImage == null || !(bool)loadImage.Invoke(null, new object[] { texture, bytes, false }))
            throw new InvalidDataException("Could not decode embedded drone texture.");
        return texture;
    }

    private static Vector3[] ReadVector3(JObject json, byte[] binary, int accessorIndex, bool flipZ)
    {
        var accessor = (JObject)json["accessors"]?[accessorIndex];
        var count = (int)accessor["count"];
        var result = new Vector3[count];
        var cursor = AccessorCursor(json, accessor, 12);
        for (var i = 0; i < count; i++)
        {
            var x = BitConverter.ToSingle(binary, cursor.offset + i * cursor.stride);
            var y = BitConverter.ToSingle(binary, cursor.offset + i * cursor.stride + 4);
            var z = BitConverter.ToSingle(binary, cursor.offset + i * cursor.stride + 8);
            result[i] = new Vector3(x, y, flipZ ? -z : z);
        }
        return result;
    }

    private static Vector2[] ReadVector2(JObject json, byte[] binary, int accessorIndex)
    {
        var accessor = (JObject)json["accessors"]?[accessorIndex];
        var count = (int)accessor["count"];
        var result = new Vector2[count];
        var cursor = AccessorCursor(json, accessor, 8);
        for (var i = 0; i < count; i++)
        {
            var u = BitConverter.ToSingle(binary, cursor.offset + i * cursor.stride);
            var v = BitConverter.ToSingle(binary, cursor.offset + i * cursor.stride + 4);
            result[i] = new Vector2(u, v);
        }
        return result;
    }

    private static int[] ReadIndices(JObject json, byte[] binary, int accessorIndex)
    {
        var accessor = (JObject)json["accessors"]?[accessorIndex];
        var count = (int)accessor["count"];
        var component = (int)accessor["componentType"];
        var width = component == 5125 ? 4 : component == 5123 ? 2 : 1;
        var cursor = AccessorCursor(json, accessor, width);
        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            var at = cursor.offset + i * cursor.stride;
            result[i] = component switch
            {
                5125 => (int)BitConverter.ToUInt32(binary, at),
                5123 => BitConverter.ToUInt16(binary, at),
                5121 => binary[at],
                _ => throw new InvalidDataException("Unsupported GLB index component type " + component)
            };
        }
        return result;
    }

    private static (int offset, int stride) AccessorCursor(JObject json, JObject accessor, int elementSize)
    {
        var view = (JObject)json["bufferViews"]?[(int)accessor["bufferView"]];
        var viewOffset = view["byteOffset"] == null ? 0 : (int)view["byteOffset"];
        var accessorOffset = accessor["byteOffset"] == null ? 0 : (int)accessor["byteOffset"];
        var stride = view["byteStride"] == null ? elementSize : (int)view["byteStride"];
        return (viewOffset + accessorOffset, stride);
    }
}
