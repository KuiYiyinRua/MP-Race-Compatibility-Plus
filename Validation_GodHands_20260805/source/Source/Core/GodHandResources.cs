using UnityEngine;
using Verse;

namespace GodHandMod
{
    [StaticConstructorOnStartup]
    public static class GodHandResources
    {
        // 缓存材质
        private static Material _shieldDomeMat;
        private static Shader _dissolveShader;
        private static Material _orbMat;
        private static Material _orbMatBlue;

        public static Shader DissolveShader
        {
            get
            {
                if (_dissolveShader == null)
                    _dissolveShader = AssetLoader.ShieldDissolveShader ?? Shader.Find("GodHand/ShieldDissolve");
                return _dissolveShader;
            }
        }

        public static Material ShieldDomeMat
        {
            get
            {
                if (_shieldDomeMat == null)
                    _shieldDomeMat = MaterialPool.MatFrom("Other/ForceField", DissolveShader ?? ShaderDatabase.MoteGlow);
                return _shieldDomeMat;
            }
        }

        public static Material OrbMat
        {
            get
            {
                if (_orbMat == null)
                    _orbMat = MaterialPool.MatFrom("Things/Mote/BrightFlash", ShaderDatabase.MoteGlow, Color.yellow);
                return _orbMat;
            }
        }

        public static Material OrbMatBlue
        {
            get
            {
                if (_orbMatBlue == null)
                    _orbMatBlue = MaterialPool.MatFrom("Things/Mote/BrightFlash", ShaderDatabase.MoteGlow, Color.cyan);
                return _orbMatBlue;
            }
        }

        // 统一描边深度偏移量
        public const float OutlineDepthOffset = 0.005f;

        // 通用描边材质
        private static Material _outlineMat;
        private static Shader _outlineShader;
        private static Shader _jfaShader;

        public static Shader JFAShader => _jfaShader ??= AssetLoader.JFAShader;

        public static Shader OutlineShader
        {
            get
            {
                if (_outlineShader == null)
                    _outlineShader = AssetLoader.ObjectOutlineShader;
                return _outlineShader;
            }
        }

        public static Material OutlineMat
        {
            get
            {
                if (_outlineMat == null)
                {
                    if (OutlineShader != null)
                    {
                        _outlineMat = new Material(OutlineShader);
                        _outlineMat.SetColor("_OutlineColor", Color.black);
                        _outlineMat.SetFloat("_OutlineWidth", 0.05f);
                    }
                    else
                    {
                        _outlineMat = SolidColorMaterials.SimpleSolidColorMaterial(Color.black);
                    }
                }
                return _outlineMat;
            }
        }

        private static Material _stencilMaskMat;
        public static Material StencilMaskMat
        {
            get
            {
                if (_stencilMaskMat == null && AssetLoader.StencilMaskShader != null)
                {
                    _stencilMaskMat = new Material(AssetLoader.StencilMaskShader);
                }
                return _stencilMaskMat;
            }
        }
    }
}
