using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    public static class ObjLoader
    {
        // 加载OBJ文件并转换为Mesh
        public static Mesh Load(string localPath, float scale)
        {
            var mod = LoadedModManager.GetMod<GodHandModMain>();
            if (mod == null)
            {
                Log.Error($"[GodHand] Could not find mod instance for file: {localPath}");
                return null;
            }

            string fullPath = Path.Combine(mod.Content.RootDir, localPath);
            if (!File.Exists(fullPath))
            {
                Log.Error($"[GodHand] OBJ file not found at: {fullPath}");
                return null;
            }

            try
            {
                return ParseObj(File.ReadAllLines(fullPath), scale);
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHand] Failed to parse OBJ {localPath}: {ex}");
                return null;
            }
        }

        private static Mesh ParseObj(string[] lines, float scale)
        {
            // 收集顶点法线
            List<Vector3> rawVerts = new List<Vector3>();
            List<Vector2> rawUVs = new List<Vector2>();
            List<Vector3> rawNormals = new List<Vector3>();

            // 面数据暂存
            List<string[]> faceLines = new List<string[]>();

            foreach (var line in lines)
            {
                if (line.Length < 2 || line[0] == '#') continue;
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                string type = parts[0];
                if (type == "v" && parts.Length >= 4)
                {
                    float x = float.Parse(parts[1]);
                    float y = float.Parse(parts[2]);
                    float z = float.Parse(parts[3]);
                    rawVerts.Add(new Vector3(x, y, z));
                }
                else if (type == "vt" && parts.Length >= 3)
                {
                    rawUVs.Add(new Vector2(float.Parse(parts[1]), float.Parse(parts[2])));
                }
                else if (type == "vn" && parts.Length >= 4)
                {
                    float x = float.Parse(parts[1]);
                    float y = float.Parse(parts[2]);
                    float z = float.Parse(parts[3]);
                    rawNormals.Add(new Vector3(x, y, z));
                }
                else if (type == "f")
                {
                    faceLines.Add(parts);
                }
            }

            if (rawVerts.Count == 0) return null;

            // 计算全局边界框中心
            Vector3 min = rawVerts[0];
            Vector3 max = rawVerts[0];
            for (int i = 1; i < rawVerts.Count; i++)
            {
                min = Vector3.Min(min, rawVerts[i]);
                max = Vector3.Max(max, rawVerts[i]);
            }
            Vector3 center = (min + max) * 0.5f;

            // 居中缩放顶点
            for (int i = 0; i < rawVerts.Count; i++)
            {
                rawVerts[i] = (rawVerts[i] - center) * scale;
            }

            // 构建最终网格
            List<Vector3> finalVerts = new List<Vector3>();
            List<Vector2> finalUVs = new List<Vector2>();
            List<Vector3> finalNorms = new List<Vector3>();
            List<int> finalTris = new List<int>();
            Dictionary<string, int> lookup = new Dictionary<string, int>();

            foreach (var parts in faceLines)
            {
                int[] faceIndices = new int[parts.Length - 1];
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string vertexData = parts[i + 1];
                    if (lookup.TryGetValue(vertexData, out int index))
                    {
                        faceIndices[i] = index;
                    }
                    else
                    {
                        string[] indices = vertexData.Split('/');
                        int vIdx = int.Parse(indices[0]) - 1;
                        int vtIdx = indices.Length > 1 && !string.IsNullOrEmpty(indices[1])
                            ? int.Parse(indices[1]) - 1 : -1;
                        int vnIdx = indices.Length > 2 && !string.IsNullOrEmpty(indices[2])
                            ? int.Parse(indices[2]) - 1 : -1;

                        Vector3 pos = rawVerts[vIdx];
                        Vector2 uv = (vtIdx >= 0 && vtIdx < rawUVs.Count)
                            ? rawUVs[vtIdx] : Vector2.zero;
                        Vector3 norm = (vnIdx >= 0 && vnIdx < rawNormals.Count)
                            ? rawNormals[vnIdx] : Vector3.up;

                        index = finalVerts.Count;
                        finalVerts.Add(pos);
                        finalUVs.Add(uv);
                        finalNorms.Add(norm);
                        lookup[vertexData] = index;
                        faceIndices[i] = index;
                    }
                }

                // 处理绕序
                for (int j = 1; j < faceIndices.Length - 1; j++)
                {
                    finalTris.Add(faceIndices[0]);
                    finalTris.Add(faceIndices[j + 1]);
                    finalTris.Add(faceIndices[j]);
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "ImportedObj";
            mesh.SetVertices(finalVerts);
            mesh.SetUVs(0, finalUVs);
            mesh.SetNormals(finalNorms);
            mesh.SetTriangles(finalTris, 0);

            // 计算平滑外扩法线并存入 Tangents
            Vector3[] smoothNormals = ComputeSmoothedNormals(finalVerts.ToArray(), finalTris.ToArray());
            Vector4[] tangents = new Vector4[smoothNormals.Length];
            for (int i = 0; i < smoothNormals.Length; i++)
                tangents[i] = new Vector4(smoothNormals[i].x, smoothNormals[i].y, smoothNormals[i].z, 1f);
            mesh.SetTangents(tangents);

            mesh.RecalculateBounds();
            if (rawNormals.Count == 0) mesh.RecalculateNormals();

            Log.Message($"[GodHand] OBJ loaded: {finalVerts.Count} verts, " +
                        $"bounds center={mesh.bounds.center}, size={mesh.bounds.size}, " +
                        $"raw center offset={center}");
            return mesh;
        }
        // 角度加权平滑法线
        private static Vector3[] ComputeSmoothedNormals(Vector3[] verts, int[] tris)
        {
            Dictionary<long, Vector3> posNormals = new Dictionary<long, Vector3>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                Vector3 v0 = verts[i0], v1 = verts[i1], v2 = verts[i2];
                Vector3 e01 = v1 - v0, e02 = v2 - v0;
                Vector3 e10 = v0 - v1, e12 = v2 - v1;
                Vector3 e20 = v0 - v2, e21 = v1 - v2;

                Vector3 faceN = -Vector3.Cross(e01, e02);
                if (faceN.sqrMagnitude < 1e-10f) continue;
                faceN.Normalize();

                float a0 = AngleBetween(e01, e02);
                float a1 = AngleBetween(e10, e12);
                float a2 = AngleBetween(e20, e21);

                AddToPos(posNormals, v0, faceN * a0);
                AddToPos(posNormals, v1, faceN * a1);
                AddToPos(posNormals, v2, faceN * a2);
            }

            Vector3[] result = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                long key = PosKey(verts[i]);
                Vector3 n = posNormals.ContainsKey(key) ? posNormals[key].normalized : Vector3.up;
                result[i] = n;
            }
            return result;
        }

        private static float AngleBetween(Vector3 a, Vector3 b)
        {
            float dot = Vector3.Dot(a.normalized, b.normalized);
            return Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
        }

        private static long PosKey(Vector3 v)
        {
            int x = Mathf.RoundToInt(v.x * 1000f);
            int y = Mathf.RoundToInt(v.y * 1000f);
            int z = Mathf.RoundToInt(v.z * 1000f);
            return ((long)x << 40) | ((long)(y & 0xFFFFF) << 20) | (long)(z & 0xFFFFF);
        }

        private static void AddToPos(Dictionary<long, Vector3> map, Vector3 pos, Vector3 val)
        {
            long key = PosKey(pos);
            if (!map.ContainsKey(key)) map[key] = Vector3.zero;
            map[key] += val;
        }
    }
}
