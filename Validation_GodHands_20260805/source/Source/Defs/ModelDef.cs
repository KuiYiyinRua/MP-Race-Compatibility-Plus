using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    public class ModelDef : Def
    {
        // 纹理映射表Key到Path
        public List<TextureMapItem> textures;
        public List<ModelElement> elements;

        // 缓存生成的模型切片
        [Unsaved(false)]
        private List<ModelPart> cachedParts;

        public List<ModelPart> GetParts(float scale = 1.0f)
        {
            if (cachedParts == null)
            {
                cachedParts = RuntimeModelBuilder.BuildPartsFromDef(this, scale);
            }
            return cachedParts;
        }

        // 辅助查找贴图路径
        public string GetTexturePath(string key)
        {
            if (textures == null) return null;
            foreach (var item in textures)
                if (item.key == key) return item.path;
            return null;
        }
    }

    public class ModelPart
    {
        public string partName;
        public Mesh mesh;
        public Mesh outlineMesh;
        public Material material;
        public bool isDynamic; // 是否为动态旋转部件
        public bool useShaderOutline; // 使用shader描边
    }

    public class TextureMapItem
    {
        public string key;
        public string path;
    }

    public class ModelElement
    {
        public string name;
        public string objPath;
        public float objScale = 16f; // 默认缩放比
        public Vector3 from;
        public Vector3 to;
        public ModelRotation rotation;
        public ModelFaces faces;
    }

    public class ModelRotation
    {
        public Vector3 origin;
        public string axis;
        public float angle;
    }

    public class ModelFaces
    {
        public ModelFace north;
        public ModelFace south;
        public ModelFace east;
        public ModelFace west;
        public ModelFace up;
        public ModelFace down;
    }

    public class ModelFace
    {
        public Vector4 uv; // UV坐标映射范围
        public string texture;
        public int rotation;
    }
}
