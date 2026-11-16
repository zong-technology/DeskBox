using System;
using System.Drawing;

namespace DeskBox.UI
{
    // One per call: actual audio drives an attack/release envelope and short travelling wavefronts.
    internal sealed class HologramDynamics
    {
        internal float Energy, Speaking, Thinking, Impulse, Interruption, Information, Flow;
        internal readonly float[] PulseAge={10,10,10}, PulseStrength=new float[3];
        private float previousTarget, sincePulse=1;
        private int nextPulse;

        internal static float FromRms(double rms)
        {
            return (float)Math.Min(1,Math.Sqrt(Math.Max(0,rms-.004)*5.5));
        }

        internal void Advance(double elapsed,HologramMode mode,float target,bool reduced)
        {
            float dt=(float)Math.Max(0,Math.Min(.1,elapsed));
            target=Math.Max(0,Math.Min(1,target));
            Energy=Ease(Energy,target,dt,target>Energy ? .045f : .23f);
            Speaking=Ease(Speaking,mode==HologramMode.Speaking ? 1 : 0,dt,.16f);
            Thinking=Ease(Thinking,mode==HologramMode.Thinking || mode==HologramMode.Connecting ? 1 : 0,dt,.24f);
            Impulse*= (float)Math.Exp(-dt/ .18f);
            Interruption=Math.Max(0,Interruption-dt*3.5f);
            Information=Math.Max(0,Information-dt*.8f);
            sincePulse+=dt;
            for(int i=0;i<PulseAge.Length;i++)PulseAge[i]+=dt;
            float rise=target-previousTarget;
            if(rise>.075f && target>.12f && sincePulse>.14f && !reduced){
                Impulse=Math.Max(Impulse,Math.Min(1,rise*2.8f));
                PulseAge[nextPulse]=0;PulseStrength[nextPulse]=Math.Min(1,.3f+target*.7f);
                nextPulse=(nextPulse+1)%PulseAge.Length;sincePulse=0;
            }
            previousTarget=target;
            if(!reduced){
                float quiet=mode==HologramMode.Muted || mode==HologramMode.Idle || mode==HologramMode.Unavailable ? .10f : .28f;
                Flow+=dt*(quiet+Thinking*.55f+Energy*(.8f+Speaking*1.4f));
            }
        }
        internal void Interrupt()
        {
            Energy=Impulse=Speaking=previousTarget=0;Interruption=1;
            for(int i=0;i<PulseAge.Length;i++)PulseAge[i]=10;
        }
        private static float Ease(float value,float target,float dt,float duration)
        {
            return value+(target-value)*(1-(float)Math.Exp(-dt/duration));
        }
    }

    internal static class HologramPalette
    {
        private static readonly Color[] colors=Build();
        internal static Color At(int index) { return colors[index & 255]; }
        private static Color[] Build()
        {
            var stops=new[]{Color.FromArgb(39,229,210),Color.FromArgb(76,199,255),Color.FromArgb(105,125,255),
                Color.FromArgb(185,114,250),Color.FromArgb(242,132,209),Color.FromArgb(255,210,138),Color.FromArgb(39,229,210)};
            var result=new Color[256];
            for(int i=0;i<result.Length;i++){
                float p=i*(stops.Length-1)/256f;int a=(int)p;float f=p-a;
                result[i]=Color.FromArgb((int)(stops[a].R+(stops[a+1].R-stops[a].R)*f),
                    (int)(stops[a].G+(stops[a+1].G-stops[a].G)*f),(int)(stops[a].B+(stops[a+1].B-stops[a].B)*f));
            }
            return result;
        }
    }
}
