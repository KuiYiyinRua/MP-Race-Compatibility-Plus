using System.Collections.Generic;
using Verse;

namespace GodHandMod
{
    // 医疗知识问答 Def
    public class GodHandQuizDef : Def
    {
        public string question;
        public List<string> options;
        public int correctIndex;
    }
}
