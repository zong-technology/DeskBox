using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DeskBox.UI
{
    // A perspective-projected light field. Positions have real depth; this is not a tinted image plane.
    internal sealed class QuantumPresence : IDisposable
    {
        private struct Light { public float X,Y,Z,Phase,Weight; public int Layer; }
        private readonly Light[] cloud = new Light[10800];
        private Bitmap sharp, bloom;
        private byte[] pixels, glow, scratch;
        private int width,height,gw,gh;
        private float scale,cx,cy,sy,cosY,sinX,cosX;
        private bool muted;
        private static readonly float[] CircleX=new float[720], CircleZ=new float[720];
        private static readonly int[] OrbitColors={24,111,63,155,8,92};

        static QuantumPresence()
        {
            for(int i=0;i<720;i++){CircleX[i]=(float)Math.Cos(i*Math.PI*2/720);CircleZ[i]=(float)Math.Sin(i*Math.PI*2/720);}
        }

        public QuantumPresence()
        {
            for(int i=0;i<cloud.Length;i++){
                double y=1-2*(i+.5)/cloud.Length, radius=Math.Sqrt(1-y*y), a=i*2.3999632297;
                int layer=i%9==0 ? 2 : i%3==0 ? 0 : 1;
                float r=layer==0 ? .17f+(i%103)/103f*.34f : layer==1 ? .76f : .96f+(i%43)/43f*.19f;
                cloud[i]=new Light {X=(float)(Math.Cos(a)*radius)*r,Y=(float)y*r,Z=(float)(Math.Sin(a)*radius)*r,
                    Phase=(i%127)/127f,Weight=layer==0 ? .30f : layer==1 ? .25f : .13f,Layer=layer};
            }
        }

        public void Draw(Graphics g,Rectangle area,double seconds,float energy,HologramMode mode,bool reduced,HologramDynamics response)
        {
            if(area.Width<4 || area.Height<4)return;
            float ratio=(float)Math.Min(1,Math.Min(1280.0/area.Width,780.0/area.Height));
            SizeBuffer(Math.Max(4,(int)(area.Width*ratio)),Math.Max(4,(int)(area.Height*ratio)));
            Array.Clear(pixels,0,pixels.Length);Array.Clear(glow,0,glow.Length);
            float time=reduced ? 0 : (float)seconds, flow=reduced ? 0 : response.Flow;
            float motion=reduced ? 0 : Math.Max(0,Math.Min(1,energy));
            float speaking=response.Speaking, thinking=response.Thinking;
            float impulse=reduced ? 0 : response.Impulse, interrupt=reduced ? 0 : response.Interruption;
            float information=reduced ? 0 : response.Information;
            float yaw=time*.055f+flow*.13f,pitch=.23f+(reduced ? 0 : (float)Math.Sin(flow*.24f)*.07f);
            sy=(float)Math.Sin(yaw);cosY=(float)Math.Cos(yaw);sinX=(float)Math.Sin(pitch);cosX=(float)Math.Cos(pitch);
            scale=Math.Min(width*.24f,height*.27f);cx=width*.5f;cy=height*.44f;
            muted=mode==HologramMode.Muted;
            float active=mode==HologramMode.Idle || mode==HologramMode.Unavailable ? .70f : muted ? .64f : 1;
            float formation=reduced ? 1 : Math.Min(1,(float)seconds/1.35f);
            float expansion=1+motion*.10f+impulse*.055f-interrupt*.11f;
            for(int i=0;i<cloud.Length;i++){
                var p=cloud[i];
                float band=(float)Math.Sin(p.Y*7-flow*2.3f+p.Phase*4);
                float breathe=expansion+(float)Math.Sin(time*.8f+p.Y*4)*.012f;
                float warp=band*(.010f+motion*.055f)*(p.Layer==0 ? .45f : 1);
                float light=p.Weight*(.84f+.16f*band)*(1+motion*.85f+information*.35f)*active;
                int hue=p.Layer==0 ? 190+(int)(p.Phase*35) : p.Layer==1 ? 4+(int)(p.Phase*53+p.Y*12) : 103+(int)(p.Phase*65);
                Point(p.X*breathe+warp,p.Y*breathe,p.Z*breathe,light*formation,hue,false);
            }
            // Independently coloured strands reveal the core behind the cool outer shell.
            for(int ribbon=0;ribbon<3;ribbon++){
                float roll=ribbon*.91f+flow*.08f,cr=(float)Math.Cos(roll),sr=(float)Math.Sin(roll);
                float phase=flow*.45f+ribbon*2.094f;
                float ct=(float)Math.Cos(phase),st=(float)Math.Sin(phase);
                for(int i=0;i<1200;i++){
                    float a=i*(float)(Math.PI*2/1200),c3=(float)Math.Cos(3*a),s3=(float)Math.Sin(3*a);
                    float radius=(.57f+.12f*(c3*ct-s3*st))*expansion;
                    float x=radius*(float)Math.Cos(2*a),y=radius*(float)Math.Sin(2*a),z=.25f*s3;
                    float crest=(float)Math.Pow(.5+.5*(s3*ct-c3*st),10);
                    float light=.39f+motion*.30f+crest*(.40f+speaking*.28f);
                    int hue=ribbon==0 ? 24 : ribbon==1 ? 117+(int)(speaking*20) : 202;
                    Point(x,y*cr-z*sr,y*sr+z*cr,light*active*formation,hue,crest>.84f && i%23==0);
                }
            }
            // Near and far arcs have separate tilts, colour families and travelling highlights.
            for(int orbit=0;orbit<6;orbit++){
                float radius=(.98f+orbit*.067f)*(1+motion*.045f-interrupt*.075f);
                float tilt=.32f+orbit*.49f+(float)Math.Sin(flow*.15f+orbit)*thinking*.10f,yawOrbit=orbit*.81f;
                float ct=(float)Math.Cos(tilt),st=(float)Math.Sin(tilt),co=(float)Math.Cos(yawOrbit),so=(float)Math.Sin(yawOrbit);
                float travel=flow*(orbit%2==0 ? .75f : -.62f)+orbit;
                float tc=(float)Math.Cos(travel),ts=(float)Math.Sin(travel);
                for(int i=0;i<720;i++){
                    if((i+orbit*37)%180>162)continue;
                    float x=radius*CircleX[i],z=radius*CircleZ[i],y=z*st;z*=ct;
                    float head=(float)Math.Pow(Math.Max(0,CircleX[i]*tc+CircleZ[i]*ts),18);
                    float light=(orbit%2==0 ? .24f : .15f)+head*(.8f+motion*.5f);
                    Point(x*co+z*so,y,z*co-x*so,light*active*formation,OrbitColors[orbit],head>.94f && i%4==0);
                }
            }
            for(int ring=0;ring<4;ring++)
                for(int i=0;i<720;i++){
                    float radius=1.17f+ring*.12f;
                    float beam=.72f+.28f*(float)Math.Cos(i*.026f-flow*(ring%2==0 ? 1 : -1));
                    Point(CircleX[i]*radius,-1.40f-ring*.025f,CircleZ[i]*radius,
                        (ring==1 ? .46f : .20f)*beam*(1+motion*.4f)*formation,ring%2==0 ? 19 : 108,false);
                }
            // Voice onsets emit expanding wavefronts; silence never creates artificial voice pulses.
            if(!reduced)for(int wave=0;wave<response.PulseAge.Length;wave++){
                float age=response.PulseAge[wave];if(age>1.25f)continue;
                float radius=1.1f+age*.72f,light=(1-age/1.25f)*response.PulseStrength[wave]*.47f;
                for(int i=0;i<720;i+=2)Point(CircleX[i]*radius,-1.42f,CircleZ[i]*radius,light*formation,speaking>.5f ? 143 : 12,false);
            }
            if(motion>.015f)for(int ray=0;ray<96;ray++){
                int index=ray*720/96;
                float band=.35f+.65f*(float)Math.Pow(.5+.5*Math.Sin(ray*.61f+flow*3.1f),2);
                float rise=motion*(.06f+band*.25f);
                for(int step=0;step<5;step++)
                    Point(CircleX[index]*1.30f,-1.40f+rise*step/4,CircleZ[index]*1.30f,
                        (.18f+motion*.32f)*(1-step/6f)*formation,ray%3==0 ? 195 : 25+(int)(speaking*95),step==4 && ray%12==0);
            }
            for(int i=0;i<180;i++){
                float progress=reduced ? (i*.618034f%1) : (i*.618034f+time*.018f+flow*.044f)%1;
                float a=i*2.39996f+flow*.13f,r=.11f+progress*.60f;
                Point((float)Math.Cos(a)*r,-1.35f+progress*2.3f,(float)Math.Sin(a)*r,
                    (float)Math.Sin(progress*Math.PI)*(.19f+motion*.2f)*formation,i%4==0 ? 202 : 20,i%19==0);
            }
            Blur();Copy(sharp,pixels);Copy(bloom,glow);
            var state=g.Save();g.SetClip(area,CombineMode.Intersect);
            Ambient(g,area,.50f,.43f,.56f,.80f,Color.FromArgb(18,48,139,244));
            Ambient(g,area,.40f,.38f,.30f,.48f,Color.FromArgb(12,33,225,204));
            Ambient(g,area,.61f,.46f,.30f,.48f,Color.FromArgb((int)(10+motion*8),191,94,239));
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawImage(bloom,area);g.DrawImage(sharp,area);g.Restore(state);
        }

        private static void Ambient(Graphics g,Rectangle area,float x,float y,float w,float h,Color color)
        {
            using(var path=new GraphicsPath()){
                path.AddEllipse(area.X+area.Width*(x-w/2),area.Y+area.Height*(y-h/2),area.Width*w,area.Height*h);
                using(var brush=new PathGradientBrush(path)){brush.CenterColor=color;brush.SurroundColors=new[]{Color.Transparent};g.FillPath(brush,path);}
            }
        }

        private void Point(float x,float y,float z,float weight,int hue,bool white)
        {
            float xx=x*cosY+z*sy,zz=z*cosY-x*sy;
            float yy=y*cosX-zz*sinX;zz=y*sinX+zz*cosX;
            float perspective=4.8f/(4.8f-zz),depth=Math.Max(.20f,Math.Min(1,.57f+zz*.29f));
            float px=cx+xx*scale*perspective,py=cy-yy*scale*perspective;
            int alpha=(int)(Math.Min(1,weight*depth)*250);
            var tint=HologramPalette.At(hue);
            int r=muted ? 128 : tint.R,g=muted ? 159 : tint.G,b=muted ? 190 : tint.B;
            if(white){r+=(255-r)*3/4;g+=(255-g)*3/4;b+=(255-b)*3/4;}
            Dot(pixels,width,height,px,py,r,g,b,alpha);
            Dot(glow,gw,gh,px/4,py/4,r,g,b,alpha/2);
            if(white && zz>0){
                Dot(pixels,width,height,px-1,py,r,g,b,alpha/5);Dot(pixels,width,height,px+1,py,r,g,b,alpha/5);
                Dot(pixels,width,height,px,py-1,r,g,b,alpha/5);Dot(pixels,width,height,px,py+1,r,g,b,alpha/5);
            }
        }
        private static void Dot(byte[] data,int w,int h,float x,float y,int r,int g,int b,int alpha)
        {
            int left=(int)Math.Floor(x),top=(int)Math.Floor(y);float dx=x-left,dy=y-top;
            Add(data,w,h,left,top,r,g,b,(int)(alpha*(1-dx)*(1-dy)));
            Add(data,w,h,left+1,top,r,g,b,(int)(alpha*dx*(1-dy)));
            Add(data,w,h,left,top+1,r,g,b,(int)(alpha*(1-dx)*dy));
            Add(data,w,h,left+1,top+1,r,g,b,(int)(alpha*dx*dy));
        }
        private static void Add(byte[] data,int w,int h,int x,int y,int r,int g,int b,int a)
        {
            if(x<0||y<0||x>=w||y>=h||a<=0)return;
            int p=(y*w+x)*4;
            data[p]=(byte)Math.Min(255,data[p]+(b*a>>8));data[p+1]=(byte)Math.Min(255,data[p+1]+(g*a>>8));
            data[p+2]=(byte)Math.Min(255,data[p+2]+(r*a>>8));data[p+3]=(byte)Math.Min(255,data[p+3]+a);
        }
        private void Blur()
        {
            for(int y=0;y<gh;y++)for(int x=0;x<gw;x++)for(int c=0;c<4;c++){
                int value=0;
                for(int k=-2;k<=2;k++){int xx=Math.Max(0,Math.Min(gw-1,x+k));int weight=k==0?6:Math.Abs(k)==1?4:1;value+=glow[(y*gw+xx)*4+c]*weight;}
                scratch[(y*gw+x)*4+c]=(byte)(value/16);
            }
            for(int y=0;y<gh;y++)for(int x=0;x<gw;x++)for(int c=0;c<4;c++){
                int value=0;
                for(int k=-2;k<=2;k++){int yy=Math.Max(0,Math.Min(gh-1,y+k));int weight=k==0?6:Math.Abs(k)==1?4:1;value+=scratch[(yy*gw+x)*4+c]*weight;}
                glow[(y*gw+x)*4+c]=(byte)(value/16);
            }
        }
        private static void Copy(Bitmap bitmap,byte[] data)
        {
            var bits=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
            try{Marshal.Copy(data,0,bits.Scan0,data.Length);}finally{bitmap.UnlockBits(bits);}
        }
        private void SizeBuffer(int w,int h)
        {
            if(sharp!=null && width==w && height==h)return;
            Dispose();width=w;height=h;gw=(w+3)/4;gh=(h+3)/4;
            sharp=new Bitmap(width,height,PixelFormat.Format32bppPArgb);bloom=new Bitmap(gw,gh,PixelFormat.Format32bppPArgb);
            pixels=new byte[width*height*4];glow=new byte[gw*gh*4];scratch=new byte[glow.Length];
        }
        public void Dispose(){if(sharp!=null)sharp.Dispose();if(bloom!=null)bloom.Dispose();sharp=bloom=null;}
    }
}
