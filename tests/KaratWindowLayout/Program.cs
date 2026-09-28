using System;
using ZasLauncherGUI.Utility;
using R = ZasLauncherGUI.Utility.KaratWindowLayoutNative.Rect;
static R Rect(int l, int t, int r, int b) => new R { Left=l, Top=t, Right=r, Bottom=b };
static void Check(R m, R w, bool special, R expected) {
    bool actual = KaratWindowLayoutNative.TrySpecialBounds(m,w,out var b);
    if(actual != special || (special && (b.Left!=expected.Left || b.Top!=expected.Top || b.Right!=expected.Right || b.Bottom!=expected.Bottom))) throw new Exception("Incorrect monitor bounds");
}
Check(Rect(0,0,1920,1080),Rect(0,0,1920,1040),false,default);
Check(Rect(0,0,5120,2880),Rect(0,0,5120,2840),false,default);
Check(Rect(0,0,5120,1440),Rect(0,0,5120,1400),true,Rect(1500,0,5120,1400));
Check(Rect(-5120,-200,0,1240),Rect(-5120,-200,0,1200),true,Rect(-3620,-200,0,1200));
Check(Rect(1920,0,7040,1440),Rect(1920,40,7000,1440),true,Rect(3420,40,7000,1440));
Check(Rect(0,0,3440,1440),Rect(0,0,3440,1400),false,default);
Console.WriteLine("PASS: 6 monitor layouts");
