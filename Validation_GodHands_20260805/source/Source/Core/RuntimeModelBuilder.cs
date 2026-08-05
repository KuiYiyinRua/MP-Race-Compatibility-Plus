using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 网格构建器
    public static class RuntimeModelBuilder
    {
        public static List<ModelPart> BuildPartsFromDef(ModelDef def, float scale)
        {
            if (def == null || def.elements.NullOrEmpty()) return new List<ModelPart>();

            List<ModelPart> parts = new List<ModelPart>();

            // 预处理纹理
            Dictionary<string, string> textureMap = new Dictionary<string, string>();
            if (def.textures != null)
            {
                foreach (var item in def.textures)
                {
                    if (!string.IsNullOrEmpty(item.key) && !string.IsNullOrEmpty(item.path))
                    {
                        string cleanKey = item.key.TrimStart('#');
                        textureMap[cleanKey] = item.path;
                    }
                }
            }

            foreach (var elem in def.elements)
            {
                BuildSingleElementParts(elem, def.defName, scale, textureMap, parts);
            }
            return parts;
        }

        private static void BuildSingleElementParts(ModelElement elem, string defName, float scale, Dictionary<string, string> textureMap, List<ModelPart> outParts)
        {
            if (elem.faces == null && string.IsNullOrEmpty(elem.objPath)) return;

            // 处理 OBJ 模型路径
            if (!string.IsNullOrEmpty(elem.objPath))
            {
                Mesh objMesh = ObjLoader.Load(elem.objPath, elem.objScale);
                if (objMesh != null)
                {
                    // 应用旋转 (rotation)
                    Vector4[] tangents = objMesh.tangents;
                    if (elem.rotation != null)
                    {
                        Vector3 axis = Vector3.up;
                        if (elem.rotation.axis == "x") axis = Vector3.right;
                        if (elem.rotation.axis == "z") axis = Vector3.forward;
                        Quaternion rot = Quaternion.AngleAxis(elem.rotation.angle, axis);

                        Vector3 pivot = elem.rotation.origin;

                        Vector3[] verts = objMesh.vertices;
                        Vector3[] rotNormals = objMesh.normals;
                        bool hasNormals = rotNormals != null && rotNormals.Length == verts.Length;
                        bool hasTangents = tangents != null && tangents.Length == verts.Length;

                        for (int i = 0; i < verts.Length; i++)
                        {
                            // 顶点旋转
                            Vector3 dir = verts[i] - pivot;
                            verts[i] = rot * dir + pivot;

                            // 法线旋转
                            if (hasNormals) rotNormals[i] = rot * rotNormals[i];
                            // 平滑法线 (Tangents) 旋转
                            if (hasTangents)
                            {
                                Vector3 t = rot * new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                                tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
                            }
                        }
                        objMesh.vertices = verts;
                        if (hasNormals) objMesh.normals = rotNormals;
                        if (hasTangents) objMesh.tangents = tangents;
                    }

                    // 应用位置偏移并减去全局中心
                    Vector3 globalCenter = new Vector3(8, 8, 8);
                    Vector3 offset = elem.from - globalCenter;
                    if (offset != Vector3.zero)
                    {
                        Vector3[] verts = objMesh.vertices;
                        for (int i = 0; i < verts.Length; i++) verts[i] += offset;
                        objMesh.vertices = verts;
                    }
                    objMesh.RecalculateBounds();

                    // 构建预生成外扩描边网格 (C# 方案)
                    Mesh outlineMesh = null;
                    if (tangents != null && tangents.Length == objMesh.vertexCount)
                    {
                        outlineMesh = new Mesh();
                        outlineMesh.name = objMesh.name + "_Outline";
                        Vector3[] vertices = objMesh.vertices;
                        Vector3[] outlineVertices = new Vector3[vertices.Length];
                        float thickness = 0.8f;

                        for (int i = 0; i < vertices.Length; i++)
                        {
                            Vector3 smoothN = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                            outlineVertices[i] = vertices[i] + smoothN * thickness;
                        }

                        int[] triangles = objMesh.triangles;
                        int[] outlineTriangles = new int[triangles.Length];
                        for (int i = 0; i < triangles.Length; i += 3)
                        {
                            outlineTriangles[i] = triangles[i];
                            outlineTriangles[i + 1] = triangles[i + 2];
                            outlineTriangles[i + 2] = triangles[i + 1];
                        }

                        outlineMesh.vertices = outlineVertices;
                        outlineMesh.uv = objMesh.uv;
                        outlineMesh.triangles = outlineTriangles;
                        outlineMesh.RecalculateBounds();
                    }

                    // 解析材质并应用贴图
                    string texKey = "Base/White";
                    if (elem.faces != null)
                    {
                        var face = elem.faces.north ?? elem.faces.south ?? elem.faces.east ??
                                   elem.faces.west ?? elem.faces.up ?? elem.faces.down;
                        if (face != null && !string.IsNullOrEmpty(face.texture)) texKey = face.texture;
                    }

                    string texPath = (textureMap != null && textureMap.TryGetValue(texKey, out string path))
                        ? path : "CreateAssets/" + texKey;

                    Material mat = MaterialPool.MatFrom(texPath, ShaderDatabase.Cutout);
                    if (mat?.mainTexture != null && (texPath.StartsWith("CreateAssets/") || texPath.Contains("GodHand")))
                    {
                        mat.mainTexture.filterMode = FilterMode.Point;
                        mat.mainTexture.anisoLevel = 0;
                    }

                    outParts.Add(new ModelPart
                    {
                        partName = elem.name,
                        mesh = objMesh,
                        outlineMesh = outlineMesh, // 传入预生成的描边网格
                        material = mat,
                        useShaderOutline = false, // 禁用 Shader 位移方案
                        isDynamic = elem.name.Contains("Crank") || elem.name.Contains("Shaft") ||
                                    elem.name.Contains("Arm") || elem.name.Contains("Grip") ||
                                    elem.name.Contains("Rotation")
                    });
                }
                return;
            }

            // 预判定动态部件
            bool isDynamic = elem.name.Contains("Crank") || elem.name.Contains("Shaft") ||
                             elem.name.Contains("Arm") || elem.name.Contains("Grip");

            Dictionary<string, List<Vector3>> vertsByTex = new Dictionary<string, List<Vector3>>();
            Dictionary<string, List<Vector3>> normsByTex = new Dictionary<string, List<Vector3>>(); // 新增法线缓存
            Dictionary<string, List<Vector2>> uvsByTex = new Dictionary<string, List<Vector2>>();
            Dictionary<string, List<int>> trisByTex = new Dictionary<string, List<int>>();

            // 构建基础顶点数据
            BuildElement(elem, scale, textureMap, vertsByTex, uvsByTex, trisByTex, normsByTex);

            // 计算全局平滑法线字典
            Dictionary<Vector3, Vector3> globalSmoothedNormals = new Dictionary<Vector3, Vector3>();
            foreach (var kvp in normsByTex)
            {
                string tex = kvp.Key;
                List<Vector3> norms = kvp.Value;
                List<Vector3> verts = vertsByTex[tex];

                for (int i = 0; i < norms.Count; i++)
                {
                    Vector3 pos = new Vector3(
                        Mathf.Round(verts[i].x * 1000f) / 1000f,
                        Mathf.Round(verts[i].y * 1000f) / 1000f,
                        Mathf.Round(verts[i].z * 1000f) / 1000f
                    );
                    if (!globalSmoothedNormals.ContainsKey(pos)) globalSmoothedNormals[pos] = Vector3.zero;
                    globalSmoothedNormals[pos] += norms[i];
                }
            }

            // 按纹理分组创建网格
            foreach (var kvp in vertsByTex)
            {
                string texPath = kvp.Key;
                List<Vector3> verts = kvp.Value;
                if (verts.Count == 0) continue;

                Mesh mesh = new Mesh();
                mesh.name = $"{defName}_{elem.name}_{System.IO.Path.GetFileNameWithoutExtension(texPath)}";

                mesh.SetVertices(verts);
                mesh.SetNormals(normsByTex[texPath]); // 使用计算好的法线
                mesh.SetUVs(0, uvsByTex[texPath]);
                mesh.SetTriangles(trisByTex[texPath], 0);
                mesh.RecalculateBounds();

                // 构建平滑偏移描边网格
                Mesh outlineMesh = new Mesh();
                outlineMesh.name = mesh.name + "_Outline";
                Vector3[] vertices = mesh.vertices;
                Vector3[] outlineVertices = new Vector3[vertices.Length];

                // 设置描边粗细
                float thickness = 0.8f;

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 pos = new Vector3(
                        Mathf.Round(vertices[i].x * 1000f) / 1000f,
                        Mathf.Round(vertices[i].y * 1000f) / 1000f,
                        Mathf.Round(vertices[i].z * 1000f) / 1000f
                    );
                    Vector3 smoothN = globalSmoothedNormals[pos].normalized;
                    outlineVertices[i] = vertices[i] + smoothN * thickness;
                }

                int[] triangles = mesh.triangles;
                int[] outlineTriangles = new int[triangles.Length];
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    outlineTriangles[i] = triangles[i];
                    outlineTriangles[i + 1] = triangles[i + 2];
                    outlineTriangles[i + 2] = triangles[i + 1];
                }

                outlineMesh.vertices = outlineVertices;
                outlineMesh.uv = mesh.uv;
                outlineMesh.triangles = outlineTriangles;
                outlineMesh.RecalculateBounds();

                Material mat = MaterialPool.MatFrom(texPath, ShaderDatabase.Cutout);

                // 本Mod资源启用点采样
                if (mat?.mainTexture != null && (texPath.StartsWith("CreateAssets/") || texPath.Contains("GodHand")))
                {
                    mat.mainTexture.filterMode = FilterMode.Point;
                    mat.mainTexture.anisoLevel = 0;
                }

                outParts.Add(new ModelPart
                {
                    partName = elem.name,
                    mesh = mesh,
                    outlineMesh = outlineMesh,
                    material = mat,
                    isDynamic = isDynamic
                });
            }
        }

        private static void BuildElement(ModelElement elem, float scale, Dictionary<string, string> textureMap,
                                       Dictionary<string, List<Vector3>> vertsDict,
                                       Dictionary<string, List<Vector2>> uvsDict,
                                       Dictionary<string, List<int>> trisDict,
                                       Dictionary<string, List<Vector3>> normsDict)
        {
            Vector3 from = elem.from;
            Vector3 to = elem.to;
            Vector3 rotOrigin = elem.rotation != null ? elem.rotation.origin : new Vector3(8, 8, 16);

            // 计算旋转中心
            Vector3 globalCenterMC = new Vector3(8, 8, 8);
            Vector3 pivot = rotOrigin;

            // 设置拉伸部件锚点
            Vector3 localOrigin = globalCenterMC;
            if (elem.name != null && elem.name.Contains("_Body"))
            {
                // 右端锚点使缩放向左延伸
                localOrigin = new Vector3(to.x, globalCenterMC.y, globalCenterMC.z);
            }

            Quaternion rot = Quaternion.identity;
            if (elem.rotation != null)
            {
                Vector3 axis = Vector3.up;
                if (elem.rotation.axis == "x") axis = Vector3.right;
                if (elem.rotation.axis == "z") axis = Vector3.forward;
                rot = Quaternion.AngleAxis(elem.rotation.angle, axis);
            }

            void AddFace(string name, ModelFace face)
            {
                if (face == null || string.IsNullOrEmpty(face.texture)) return;

                // 解析贴图路径
                string texKey = face.texture.TrimStart('#');
                string texPath = "Base/White";
                if (textureMap != null && textureMap.ContainsKey(texKey))
                    texPath = textureMap[texKey];
                else
                    texPath = "CreateAssets/" + texKey;

                // 检查纹理列表
                if (!vertsDict.ContainsKey(texPath))
                {
                    vertsDict[texPath] = new List<Vector3>();
                    normsDict[texPath] = new List<Vector3>();
                    uvsDict[texPath] = new List<Vector2>();
                    trisDict[texPath] = new List<int>();
                }

                List<Vector3> verts = vertsDict[texPath];
                List<Vector3> norms = normsDict[texPath];
                List<Vector2> uvs = uvsDict[texPath];
                List<int> tris = trisDict[texPath];

                // 获取面法线并应用旋转
                Vector3 baseNormal = Vector3.up;
                if (name == "north") baseNormal = Vector3.back;
                if (name == "south") baseNormal = Vector3.forward;
                if (name == "east") baseNormal = Vector3.right;
                if (name == "west") baseNormal = Vector3.left;
                if (name == "down") baseNormal = Vector3.down;
                Vector3 finalNormal = rot * baseNormal;

                // 获取顶点坐标
                Vector3[] faceVertsIs = GetFaceVerts(name, from, to);

                // 变换处理
                for (int i = 0; i < 4; i++)
                {
                    Vector3 v = faceVertsIs[i];
                    Vector3 dir = v - pivot;
                    dir = rot * dir;
                    // 计算组件锚点
                    Vector3 modelPos = (dir + pivot - localOrigin);

                    Vector3 finalPos = modelPos * scale;
                    verts.Add(finalPos);
                    norms.Add(finalNormal);
                }

                // UV坐标
                Vector2[] faceUVs = GetFaceUVs(name, face.uv, face.rotation);
                uvs.AddRange(faceUVs);

                // 三角形索引
                int count = verts.Count;
                tris.Add(count - 4);
                tris.Add(count - 3);
                tris.Add(count - 2);

                tris.Add(count - 4);
                tris.Add(count - 2);
                tris.Add(count - 1);
            }

            AddFace("north", elem.faces?.north);
            AddFace("south", elem.faces?.south);
            AddFace("east", elem.faces?.east);
            AddFace("west", elem.faces?.west);
            AddFace("up", elem.faces?.up);
            AddFace("down", elem.faces?.down);
        }

        private static Vector3[] GetFaceVerts(string faceName, Vector3 from, Vector3 to)
        {
            float x1 = from.x, x2 = to.x;
            float y1 = from.y, y2 = to.y;
            float z1 = from.z, z2 = to.z;

            if (faceName == "north") // 朝向负Z
                return new Vector3[] { new Vector3(x2, y2, z1), new Vector3(x2, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y2, z1) };
            if (faceName == "south") // 朝向正Z
                return new Vector3[] { new Vector3(x1, y2, z2), new Vector3(x1, y1, z2), new Vector3(x2, y1, z2), new Vector3(x2, y2, z2) };
            if (faceName == "east") // 朝向正X
                return new Vector3[] { new Vector3(x2, y2, z2), new Vector3(x2, y1, z2), new Vector3(x2, y1, z1), new Vector3(x2, y2, z1) };
            if (faceName == "west") // 朝向负X
                return new Vector3[] { new Vector3(x1, y2, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z2), new Vector3(x1, y2, z2) };
            if (faceName == "up") // 朝向正Y
                return new Vector3[] { new Vector3(x1, y2, z1), new Vector3(x1, y2, z2), new Vector3(x2, y2, z2), new Vector3(x2, y2, z1) };
            if (faceName == "down") // 朝向负Y
                return new Vector3[] { new Vector3(x1, y1, z1), new Vector3(x2, y1, z1), new Vector3(x2, y1, z2), new Vector3(x1, y1, z2) };
            return new Vector3[4];
        }

        private static Vector2[] GetFaceUVs(string faceName, Vector4 uvRaw, int rotation)
        {
            if (uvRaw == Vector4.zero) uvRaw = new Vector4(0, 0, 16, 16);

            float uMin = uvRaw.x / 16f;
            float vTop = (16f - uvRaw.y) / 16f;
            float uMax = uvRaw.z / 16f;
            float vBot = (16f - uvRaw.w) / 16f;

            // 顺时针定义UV
            Vector2[] uvs;
            if (faceName == "up")
                uvs = new Vector2[] { new Vector2(uMin, vBot), new Vector2(uMin, vTop), new Vector2(uMax, vTop), new Vector2(uMax, vBot) };
            else if (faceName == "down")
                uvs = new Vector2[] { new Vector2(uMin, vBot), new Vector2(uMax, vBot), new Vector2(uMax, vTop), new Vector2(uMin, vTop) };
            else
                uvs = new Vector2[] { new Vector2(uMax, vTop), new Vector2(uMax, vBot), new Vector2(uMin, vBot), new Vector2(uMin, vTop) };

            // 处理旋转
            if (rotation != 0)
            {
                int shift = (rotation / 90) % 4;
                Vector2[] rotated = new Vector2[4];
                for (int i = 0; i < 4; i++)
                {
                    rotated[(i + shift) % 4] = uvs[i];
                }
                return rotated;
            }

            return uvs;
        }
        // 角度加权平滑法线
        private static Vector3[] ComputeSmoothedNormals(Vector3[] verts, int[] tris)
        {
            // 按位置累加角度加权法线
            Dictionary<long, Vector3> posNormals = new Dictionary<long, Vector3>();

            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                Vector3 v0 = verts[i0], v1 = verts[i1], v2 = verts[i2];

                Vector3 e01 = v1 - v0, e02 = v2 - v0;
                Vector3 e10 = v0 - v1, e12 = v2 - v1;
                Vector3 e20 = v0 - v2, e21 = v1 - v2;

                // 面法线 (CW绕序取反)
                Vector3 faceN = -Vector3.Cross(e01, e02);
                if (faceN.sqrMagnitude < 1e-10f) continue;
                faceN.Normalize();

                // 各顶点夹角作为权重
                float a0 = AngleBetween(e01, e02);
                float a1 = AngleBetween(e10, e12);
                float a2 = AngleBetween(e20, e21);

                AddToPos(posNormals, v0, faceN * a0);
                AddToPos(posNormals, v1, faceN * a1);
                AddToPos(posNormals, v2, faceN * a2);
            }

            // 写回每个顶点
            Vector3[] result = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                long key = PosKey(verts[i]);
                Vector3 n = posNormals.ContainsKey(key) ? posNormals[key].normalized : Vector3.up;
                if (n.sqrMagnitude < 0.001f) n = Vector3.up;
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
            // 高精度位置哈希
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
