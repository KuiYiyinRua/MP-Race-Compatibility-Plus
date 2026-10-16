using System;
using Verse;

internal static class MoteLifetimeRegression
{
    static int Main()
    {
        var def=new FleckDef { fadeInTime=0,solidTime=1,fadeOutTime=0.5f };
        var old=new FleckStatic { def=def,solidTimeOverride=-1 };
        var normal=new FleckStatic { def=def,solidTimeOverride=-1 };
        bool removedOld=false,removedNormal=false;
        for(int tick=1;tick<=600;tick++)
        {
            if(tick%30==0 && !removedOld)removedOld=old.TimeInterval(1f/60f,null);
            if(!removedNormal)removedNormal=normal.TimeInterval(1f/60f,null);
        }
        Console.WriteLine("NATIVE_FLECK_BASELINE ticks=600 interval=30 age="+old.ageSecs+" removed="+removedOld+" expectedLife="+old.Lifespan);
        Console.WriteLine("NATIVE_FLECK_CONTROL ticks=600 interval=1 age="+normal.ageSecs+" removed="+removedNormal);
        if(removedOld || !removedNormal || old.ageTicks!=20)throw new Exception("baseline reproduction failed");
        Console.WriteLine("PASS: skipping MapPostTick extends native fleck lifetime; every-map-tick progression removes it.");
        return 0;
    }
}
