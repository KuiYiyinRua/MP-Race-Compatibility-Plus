using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using System.Reflection;

namespace GodHandMod
{
    // 物体组件树
    public class ThingComponentTree
    {
        // 组件节点类
        public class ComponentNode
        {
            public string name;
            public string displayName;
            public object component;
            public List<ComponentNode> children = new List<ComponentNode>();
            public GraphicTransformManager.TransformData transform;

            public ComponentNode(string name, string displayName, object component = null)
            {
                this.name = name;
                this.displayName = displayName;
                this.component = component;
            }
        }

        public Thing thing;
        public ComponentNode root;

        public ThingComponentTree(Thing thing)
        {
            this.thing = thing;
            BuildTree();
        }

        // 构建组件树
        private void BuildTree()
        {
            root = new ComponentNode("root", thing.LabelCap);
            root.transform = GraphicTransformManager.GetOrCreate(thing);

            // 识别炮塔组件
            if (thing is Building_Turret turret)
            {
                AddTurretComponents(turret, root);
            }
        }

        // 添加炮塔组件
        private void AddTurretComponents(Building_Turret turret, ComponentNode parent)
        {
            try
            {
                // 获取顶部组件
                var topField = FindTopField(turret.GetType());
                if (topField != null)
                {
                    object turretTop = topField.GetValue(turret);
                    if (turretTop != null)
                    {
                        ComponentNode topNode = new ComponentNode("turretTop", "炮塔顶部", turretTop);
                        string turretTopKey = $"{turret.thingIDNumber}_turretTop";
                        topNode.transform = GraphicTransformManager.GetOrCreateByKey(turretTopKey);
                        parent.children.Add(topNode);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHand] 结构识别失败 {ex}");
            }
        }

        // 递归查找顶部字段
        private FieldInfo FindTopField(Type type)
        {
            if (type == null) return null;
            var field = type.GetField("top", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field ?? FindTopField(type.BaseType);
        }

        // 获取节点列表
        public List<ComponentNode> GetAllNodes()
        {
            List<ComponentNode> nodes = new List<ComponentNode>();
            CollectNodes(root, nodes);
            return nodes;
        }

        // 递归收集节点
        private void CollectNodes(ComponentNode node, List<ComponentNode> result)
        {
            result.Add(node);
            foreach (var child in node.children)
            {
                CollectNodes(child, result);
            }
        }
    }
}
