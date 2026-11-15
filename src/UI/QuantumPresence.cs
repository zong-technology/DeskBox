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
        private struct Light { public float X,Y,Z,Phase,Weight; }
        private readonly Light[] cloud = new Light[10800];
        private Bitmap sharp, bloom;
        private byte[] pixels, glow, scratch;
        private int width,height,gw,gh;
        private float scale,cx,cy,sy,cosY,sinX,cosX;
        private int red,green,blue;

        public QuantumPresence()
        {
            for(int i=0;i<cloud.Length;i++){
                double y=1-2*(i+.5)/cloud.Length, radius=Math.Sqrt(1-y*y), a=i*2.3999632297;
                bool inner=i%3==0;
                float r=inner ? .26f+(i%103)/103f*.31f : .76f;
                cloud[i]=new Light {X=(float)(Math.Cos(a)*radius)*r,Y=(float)y*r,Z=(float)(Math.Sin(a)*radius)*r,
                    Phase=(i%127)/127f,Weight=inner ? .11f : .26f};
            }
        }

        public void Draw(Graphics g,Rectangle area,double seconds,float energy,HologramMode mode,bool reduced)
        {
            if(area.Width<4 || area.Height<4)return;
            float ratio=(float)Math.Min(1,Math.Min(1400.0/area.Width,850.0/area.Height));
            SizeBuffer(Math.Max(4,(int)(area.Width*ratio)),Math.Max(4,(int)(area.Height*ratio)));
            Array.Clear(pixels,0,pixels.Length);Array.Clear(glow,0,glow.Length);
            float time=reduced ? 0 : (float)seconds;
            float motion=reduced ? 0 : Math.Max(0,Math.Min(1,energy));
            float yaw=time*.115f,pitch=.24f;
            sy=(float)Math.Sin(yaw);cosY=(float)Math.Cos(yaw);sinX=(float)Math.Sin(pitch);cosX=(float)Math.Cos(pitch);
            scale=Math.Min(width*.23f,height*.26f);cx=width*.5f;cy=height*.44f;
            red=mode==HologramMode.Speaking ? 142 : mode==HologramMode.Thinking ? 167 : 68;
            green=mode==HologramMode.Speaking ? 188 : mode==HologramMode.Thinking ? 162 : 217;
            blue=255;
            float active=mode==HologramMode.Idle || mode==HologramMode.Unavailable ? .62f : 1;
            float formation=reduced ? 1 : Math.Min(1,(float)seconds/1.35f);
            for(int i=0;i<cloud.Length;i++){
                var p=cloud[i];
                float breathe=1+(float)Math.Sin(time*.8f+p.Y*4)*.018f+motion*.035f;
                float warp=(float)Math.Sin(time*.42f+p.Y*8+p.Phase*3)*.025f;
                float light=p.Weight*(.80f+.20f*(float)Math.Sin(time*.5f+p.Phase*22))*(1+motion*.8f)*active;
                Point(p.X*breathe+warp,p.Y*breathe,p.Z*breathe,light*formation,p.Phase,false);
            }
            // Three interlaced, slowly travelling filaments form the interior intelligence field.
            for(int ribbon=0;ribbon<3;ribbon++)
                for(int i=0;i<1600;i++){
                    float a=i*(float)(Math.PI*2/1600)+ribbon*2.094f;
                    float r=.58f+.12f*(float)Math.Cos(3*a+time*.16f);
                    float x=r*(float)Math.Cos(2*a),y=r*(float)Math.Sin(2*a),z=.24f*(float)Math.Sin(3*a);
                    float roll=ribbon*.78f;
                    float yy=y*(float)Math.Cos(roll)-z*(float)Math.Sin(roll),zz=y*(float)Math.Sin(roll)+z*(float)Math.Cos(roll);
                    float beat=.38f+motion*.28f+(float)Math.Pow(.5+.5*Math.Sin(a*3-time*.6),12)*.25f;
                    Point(x,yy,zz,beat*active*formation,.5f,ribbon==0 && i%37==0);
                }
            // Tilted orbits use separate axes and depth; their intersections drift in perspective.
            for(int orbit=0;orbit<6;orbit++){
                float radius=.96f+orbit*.072f, tilt=.35f+orbit*.49f, yawOrbit=orbit*.81f;
                float ct=(float)Math.Cos(tilt),st=(float)Math.Sin(tilt),co=(float)Math.Cos(yawOrbit),so=(float)Math.Sin(yawOrbit);
                for(int i=0;i<900;i++){
                    if((i+orbit*43)%225>199)continue;
                    float a=i*(float)(Math.PI*2/900)+time*(orbit%2==0 ? .045f : -.035f);
                    float x=radius*(float)Math.Cos(a),z=radius*(float)Math.Sin(a),y=z*st;
                    z*=ct;
                    float dash=i%75<4 ? .8f : orbit%2==0 ? .28f : .13f;
                    Point(x*co+z*so,y,z*co-x*so,dash*formation,.75f,false);
                }
            }
            // The emitter stays part of the projected object, without an interface frame or labels.
            for(int ring=0;ring<4;ring++)
                for(int i=0;i<1000;i++){
                    float a=i*(float)(Math.PI*2/1000)+time*(ring%2==0 ? .025f : -.03f);
                    float radius=1.17f+ring*.12f;
                    Point((float)Math.Cos(a)*radius,-1.40f-ring*.025f,(float)Math.Sin(a)*radius,
                        (ring==1 ? .35f : .12f)*(1+motion*.25f)*formation,.2f,false);
                }
            for(int i=0;i<150;i++){
                float progress=reduced ? (i*.618034f%1) : (i*.618034f+time*.034f)%1;
                float a=i*2.39996f+time*.055f, r=.11f+progress*.54f;
                Point((float)Math.Cos(a)*r,-1.35f+progress*2.3f,(float)Math.Sin(a)*r,
                    (float)Math.Sin(progress*Math.PI)*.14f*formation,.4f,i%17==0);
            }
            Blur();
            Copy(sharp,pixels);Copy(bloom,glow);
            var state=g.Save();
            g.SetClip(area,CombineMode.Intersect);
            using(var path=new GraphicsPath()){
                path.AddEllipse(area.X+area.Width*.22f,area.Y+area.Height*.12f,area.Width*.56f,area.Height*.78f);
                using(var ambient=new PathGradientBrush(path)){
                    ambient.CenterColor=Color.FromArgb(17,red,green,blue);ambient.SurroundColors=new[]{Color.Transparent};
                    g.FillPath(ambient,path);
                }
            }
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawImage(bloom,area);g.DrawImage(sharp,area);
            g.Restore(state);
        }

        private void Point(float x,float y,float z,float weight,float tint,bool white)
        {
            float xx=x*cosY+z*sy,zz=z*cosY-x*sy;
            float yy=y*cosX-zz*sinX;zz=y*sinX+zz*cosX;
            float perspective=4.8f/(4.8f-zz);
            float px=cx+xx*scale*perspective,py=cy-yy*scale*perspective;
            float depth=.60f+(zz+1.6f)*.125f;
            int alpha=(int)(Math.Min(1,weight*depth)*235);
            int r=white ? 225 : Math.Min(245,red+(int)(tint*22));
            int g=white ? 246 : green;
            Dot(pixels,width,height,px,py,r,g,blue,alpha);
            Dot(glow,gw,gh,px/4,py/4,r,g,blue,alpha/3);
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
