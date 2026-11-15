using System;
using System.Windows.Forms;
namespace DeskBox.UI
{
    public static class Dpi
    {
        public static int D(int designPx){return D(Form.ActiveForm,designPx);}
        public static int D(Control owner,int designPx)
        {
            int dpi=owner==null?96:owner.DeviceDpi;
            return (int)Math.Round(designPx*dpi/96f,MidpointRounding.AwayFromZero);
        }
    }
}
