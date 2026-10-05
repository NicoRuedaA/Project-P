using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmeraldArena.Vfx
{
    [ExecuteAlways]
    public sealed class EmeraldVfxPlayer : MonoBehaviour
    {
        public TextAsset clip;
        public Material effectMaterial;
        [Min(.05f)] public float duration = 2f;
        public bool playOnEnable = true;
        public bool loop;
        [Range(0, 1)] public float previewTime = .4f;
        public float ImpactStrength { get; private set; }
        public int MoveId { get; private set; }
        public int FrameCount => offsets == null ? 0 : offsets.Length;
        public int MeshCount => meshes == null ? 0 : meshes.Length;
        public int VisibleMeshCount { get; private set; }
        public bool IsPlaying { get; private set; }
        private TextAsset loaded;
        private Material loadedMaterial;
        private Mesh[] meshes;
        private BinaryReader reader;
        private long[] offsets;
        private float elapsed;
        private readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
        private MaterialPropertyBlock block;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static void Release(UnityEngine.Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        private void OnEnable() { elapsed = 0; IsPlaying = playOnEnable && Application.isPlaying; }
        private void OnDisable() { Unload(); }
        private void Unload()
        {
            reader?.Dispose(); reader = null;
            if (meshes != null) foreach (var mesh in meshes) Release(mesh);
            meshes = null; offsets = null;
            foreach (var material in materials.Values) Release(material);
            materials.Clear(); loaded = null; loadedMaterial = null;
        }
        private float[] ReadFloats()
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 10000000) throw new InvalidDataException("Invalid geometry length");
            var a = new float[count]; for (int i = 0; i < count; i++) a[i] = reader.ReadSingle(); return a;
        }
        public void LoadClip()
        {
            if (clip == loaded && effectMaterial == loadedMaterial && reader != null) return;
            Unload();
            if (!clip || !effectMaterial) return;
            try
            {
                using (var source = new MemoryStream(clip.bytes))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                {
                    var data = new MemoryStream(); gzip.CopyTo(data); data.Position = 0;
                    reader = new BinaryReader(data);
                }
                if (reader.ReadInt32() != 0x31584645) throw new InvalidDataException("Unknown Emerald VFX format");
                MoveId = reader.ReadInt32(); int count = reader.ReadInt32(); reader.ReadInt32(); int geometryCount = reader.ReadInt32();
                if (count < 2 || count > 10000 || geometryCount < 1 || geometryCount > 10000) throw new InvalidDataException("Invalid VFX header");
                meshes = new Mesh[geometryCount];
                for (int g = 0; g < geometryCount; g++)
                {
                    var p = ReadFloats(); var n = ReadFloats(); var uv = ReadFloats();
                    var verts = new Vector3[p.Length / 3]; var normals = new Vector3[verts.Length]; var tex = new Vector2[verts.Length];
                    for (int j = 0; j < verts.Length; j++)
                    {
                        verts[j] = new Vector3(p[j*3],p[j*3+1],p[j*3+2]);
                        normals[j] = new Vector3(n[j*3],n[j*3+1],n[j*3+2]);
                        tex[j] = new Vector2(uv[j*2],uv[j*2+1]);
                    }
                    var indices = new int[reader.ReadInt32()]; for (int j = 0; j < indices.Length; j++) indices[j] = reader.ReadInt32();
                    var mesh = new Mesh { name = "Emerald VFX " + MoveId + "/" + g, indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
                    mesh.vertices = verts; mesh.normals = normals; mesh.uv = tex; mesh.triangles = indices; mesh.RecalculateBounds(); meshes[g] = mesh;
                }
                offsets = new long[count];
                for (int f = 0; f < count; f++)
                {
                    offsets[f] = reader.BaseStream.Position;
                    int draws = reader.ReadInt32(); reader.ReadSingle();
                    if (draws < 0 || draws > 100000) throw new InvalidDataException("Invalid frame size");
                    reader.BaseStream.Seek(draws * 76L, SeekOrigin.Current);
                    if (reader.BaseStream.Position > reader.BaseStream.Length) throw new InvalidDataException("Truncated frame");
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing bytes");
                loaded = clip; loadedMaterial = effectMaterial;
            }
            catch { Unload(); throw; }
        }
        [ContextMenu("Play effect")]
        public void Play() { LoadClip(); elapsed = 0; previewTime = 0; IsPlaying = true; }
        public void Stop() { IsPlaying = false; previewTime = 1; }
        /// <summary>Ground anchors. The source's six-unit +X axis is stretched between them.</summary>
        public void SetEndpoints(Vector3 attacker, Vector3 target, float heightScale = 1)
        {
            var delta = target - attacker;
            if (delta.sqrMagnitude < .000001f) throw new ArgumentException("VFX endpoints must differ");
            transform.position = (attacker + target) * .5f;
            transform.rotation = Quaternion.FromToRotation(Vector3.right, delta.normalized);
            transform.localScale = new Vector3(delta.magnitude / 6f, heightScale, heightScale);
        }
        private void Update()
        {
            if (Application.isPlaying && IsPlaying)
            {
                elapsed += Time.deltaTime;
                previewTime = loop ? Mathf.Repeat(elapsed / duration, 1) : Mathf.Clamp01(elapsed / duration);
                if (!loop && elapsed >= duration) IsPlaying = false;
            }
            RenderAt(previewTime);
        }
        public void RenderAt(float normalizedTime, Camera onlyCamera = null, bool submitDraws = true)
        {
            LoadClip(); if (reader == null) return;
            if (submitDraws && block == null) block = new MaterialPropertyBlock();
            int f = Mathf.Clamp(Mathf.RoundToInt(normalizedTime * (offsets.Length - 1)), 0, offsets.Length - 1);
            reader.BaseStream.Position = offsets[f]; int count = reader.ReadInt32();
            ImpactStrength = reader.ReadSingle(); VisibleMeshCount = count;
            for (int i = 0; i < count; i++)
            {
                reader.ReadInt32(); int geometry = reader.ReadInt32(), mode = reader.ReadInt32(), order = reader.ReadInt32(), flags = reader.ReadInt32();
                var p = new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                var q = new Quaternion(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                var s = new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                var c = new Color(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                if (geometry < 0 || geometry >= meshes.Length || !float.IsFinite(p.x+p.y+p.z+q.x+q.y+q.z+q.w+s.x+s.y+s.z+c.r+c.g+c.b+c.a)) throw new InvalidDataException("Invalid VFX draw record");
                if (!submitDraws) continue;
                int key = mode * 100000 + (flags & 3) * 10000 + Mathf.Clamp(order, 0, 9999);
                if (!materials.TryGetValue(key, out var material))
                {
                    material = new Material(effectMaterial) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 + Mathf.Clamp(order,0,900) };
                    material.SetFloat("_ZWrite",(flags & 2) != 0 ? 1 : 0);
                    material.SetFloat("_ZTest",(flags & 1) != 0 ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual);
                    material.SetFloat("_Cull", mode == 5 ? (float)CullMode.Front : (float)CullMode.Off);
                    materials.Add(key,material);
                }
                block.Clear(); block.SetColor(ColorId,c); block.SetFloat(ModeId,mode);
                Graphics.DrawMesh(meshes[geometry], transform.localToWorldMatrix * Matrix4x4.TRS(p,q,s),material,gameObject.layer,onlyCamera,0,block,ShadowCastingMode.Off,false,null,LightProbeUsage.Off);
            }
        }
    }
}
