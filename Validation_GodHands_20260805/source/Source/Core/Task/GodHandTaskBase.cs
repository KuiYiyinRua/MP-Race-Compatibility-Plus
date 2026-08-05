using UnityEngine;
using Verse;

namespace GodHandMod
{
    // 任务基类
    public abstract class GodHandTaskBase
    {
        public bool IsFinished { get; protected set; }
        public virtual void Tick() { }
        public abstract void Update(float deltaTime);
        public abstract void Draw();
        public virtual void ForceEnd() => IsFinished = true;

        // 共享光团材质
        protected static Material OrbMat => GodHandResources.OrbMat;
    }
}
